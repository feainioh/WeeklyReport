// [新增-离线存储] Phase 1 切片 C：SQLite 版周报仓储（ADR-02/03/05）。
// 与 MySQL 版语义一致：周起始唯一（一周一份）、updated_at_utc 乐观并发守护（更新/删除需匹配期望时间戳）、
// 起止日期一致性校验。时间戳比较为固定格式 TEXT 等值比较（微秒口径与 MySQL DATETIME(6) 截断行为一致）。
using System.Globalization;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

public sealed class SqliteWeeklyReportRepository(string databasePath) : IWeeklyReportRepository
{
    private const string SelectColumns =
        "id AS Id, week_start AS WeekStart, week_end AS WeekEnd, work_summary AS WorkSummary, " +
        "next_week_plan AS NextWeekPlan, risks AS Risks, confirmed_minutes AS ConfirmedMinutes, " +
        "estimated_minutes AS EstimatedMinutes, unknown_hours_count AS UnknownHoursCount, " +
        "created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc";

    private readonly SqliteExecutor _executor = new(databasePath);

    public async Task<WeeklyReportSnapshot?> FindByWeekAsync(WeekPeriod week)
    {
        var start = week.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await _executor.ExecuteAsync(async database =>
        {
            var rows = await database.Ado.SqlQueryAsync<SqliteWeeklyReportRow>(
                $"SELECT {SelectColumns} FROM weekly_reports WHERE week_start = @start",
                new { start });
            var row = rows.FirstOrDefault();
            return row is null ? null : ToReport(row);
        });
    }

    public async Task<IReadOnlyList<WeeklyReportSnapshot>> ListAsync() =>
        await _executor.ExecuteAsync<IReadOnlyList<WeeklyReportSnapshot>>(async database =>
        {
            var rows = await database.Ado.SqlQueryAsync<SqliteWeeklyReportRow>(
                $"SELECT {SelectColumns} FROM weekly_reports ORDER BY week_start DESC");
            return rows.Select(ToReport).ToArray();
        });

    // [新增-离线存储] Phase 1 切片 F：写路径入队化（业务写入与 sync_operation 同队列事务；旧独立连接写法从略，见 git 历史）
    public async Task AddAsync(WeeklyReportSnapshot report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateText(report);
        var id = report.Id.ToString("D");
        var weekStart = report.Week.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var weekEnd = report.Week.EndInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var createdAt = SqliteTimestamp.Format(report.CreatedAtUtc);
        var updatedAt = SqliteTimestamp.Format(report.UpdatedAtUtc);
        await new SqliteOperationQueue(databasePath).RunInOperationAsync("weekly-report.save", async session =>
        {
            await session.Database.Ado.ExecuteCommandAsync(
                "INSERT INTO weekly_reports (id, week_start, week_end, work_summary, next_week_plan, risks, " +
                "confirmed_minutes, estimated_minutes, unknown_hours_count, created_at_utc, updated_at_utc) " +
                "VALUES (@id, @weekStart, @weekEnd, @workSummary, @nextWeekPlan, @risks, " +
                "@confirmedMinutes, @estimatedMinutes, @unknownHoursCount, @createdAt, @updatedAt)",
                new
                {
                    id, weekStart, weekEnd,
                    workSummary = report.WorkSummary, nextWeekPlan = report.NextWeekPlan, risks = report.Risks,
                    confirmedMinutes = report.ConfirmedMinutes,
                    estimatedMinutes = report.EstimatedMinutes,
                    unknownHoursCount = report.UnknownHoursCount, createdAt, updatedAt
                });
            session.Record(new LocalOperationChange("weekly_reports", id, weekStart, LocalObjectChange.Add)
            {
                AfterJson = SqliteOperationPayload.SerializeWeeklyReport(report)
            });
            return true;
        });
    }

    public async Task<bool> UpdateAsync(WeeklyReportSnapshot report, DateTimeOffset expectedUpdatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateText(report);
        var id = report.Id.ToString("D");
        var expected = SqliteTimestamp.Format(expectedUpdatedAtUtc);
        var updatedAt = SqliteTimestamp.Format(report.UpdatedAtUtc);
        var weekStart = report.Week.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await new SqliteOperationQueue(databasePath).RunInOperationAsync("weekly-report.save", async session =>
        {
            var existingRows = await session.Database.Ado.SqlQueryAsync<SqliteWeeklyReportRow>(
                $"SELECT {SelectColumns} FROM weekly_reports WHERE id = @id",
                new { id });
            var existing = existingRows.FirstOrDefault();
            var changed = await session.Database.Ado.ExecuteCommandAsync(
                "UPDATE weekly_reports SET work_summary = @workSummary, next_week_plan = @nextWeekPlan, " +
                "risks = @risks, updated_at_utc = @updatedAt WHERE id = @id AND updated_at_utc = @expected",
                new
                {
                    id, expected, updatedAt,
                    workSummary = report.WorkSummary, nextWeekPlan = report.NextWeekPlan, risks = report.Risks
                }) > 0;
            if (changed)
            {
                var previous = existing
                    ?? throw new InvalidOperationException("更新成功但未能读取周报的变更前快照。");
                session.Record(new LocalOperationChange("weekly_reports", id, weekStart, LocalObjectChange.Update)
                {
                    BeforeJson = SqliteOperationPayload.SerializeWeeklyReport(ToReport(previous)),
                    AfterJson = SqliteOperationPayload.SerializeWeeklyReport(report)
                });
            }

            return changed;
        });
    }

    public async Task<bool> DeleteAsync(Guid id, DateTimeOffset expectedUpdatedAtUtc)
    {
        var key = id.ToString("D");
        var expected = SqliteTimestamp.Format(expectedUpdatedAtUtc);
        return await new SqliteOperationQueue(databasePath).RunInOperationAsync("weekly-report.delete", async session =>
        {
            var rows = await session.Database.Ado.SqlQueryAsync<SqliteWeeklyReportRow>(
                $"SELECT {SelectColumns} FROM weekly_reports WHERE id = @key AND updated_at_utc = @expected",
                new { key, expected });
            var row = rows.FirstOrDefault();
            if (row is null)
            {
                return false; // 行不存在或乐观并发不匹配：不入队
            }

            await session.Database.Ado.ExecuteCommandAsync(
                "DELETE FROM weekly_reports WHERE id = @key AND updated_at_utc = @expected",
                new { key, expected });
            var previous = ToReport(row);
            session.Record(new LocalOperationChange(
                "weekly_reports", key, row.WeekStart, LocalObjectChange.Delete)
            {
                BeforeJson = SqliteOperationPayload.SerializeWeeklyReport(previous)
            });
            return true;
        });
    }

    internal static WeeklyReportSnapshot ToReport(SqliteWeeklyReportRow row)
    {
        var week = new WeekPeriod(DateOnly.ParseExact(row.WeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (DateOnly.ParseExact(row.WeekEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture) != week.EndInclusive)
        {
            throw new InvalidDataException("周报的起止日期不一致，请检查本地存储。");
        }

        return new WeeklyReportSnapshot(
            Guid.Parse(row.Id),
            week,
            row.WorkSummary,
            row.NextWeekPlan,
            row.Risks,
            row.ConfirmedMinutes,
            row.EstimatedMinutes,
            row.UnknownHoursCount,
            SqliteTimestamp.Parse(row.CreatedAtUtc),
            SqliteTimestamp.Parse(row.UpdatedAtUtc));
    }

    private static void ValidateText(WeeklyReportSnapshot report)
    {
        DbTextLimits.Validate(report.WorkSummary, "本周工作");
        DbTextLimits.Validate(report.NextWeekPlan, "下周计划");
        DbTextLimits.Validate(report.Risks, "问题与风险");
    }
}
