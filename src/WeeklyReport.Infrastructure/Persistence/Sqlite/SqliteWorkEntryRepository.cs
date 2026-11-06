// [新增-离线存储] Phase 1 切片 A：SQLite 版工作记录仓储（ADR-02/05）。
// 与 MySQL 版差异：日期/时间戳按 ADR-05 以固定格式 TEXT 存储——
//   work_date = yyyy-MM-dd；created/updated_at_utc = yyyy-MM-ddTHH:mm:ss.ffffffZ（微秒口径）
// 不使用 SqlSugar 的 DateTime 映射（其 SQLite 序列化仅保留毫秒，会丢失微秒精度）。
// SQL 为 SQLite 方言专用、全部参数化，不与 MySQL 共享（ADR-02 约束）。
using System.Globalization;
using Microsoft.Data.Sqlite;
using SqlSugar;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

public sealed class SqliteWorkEntryRepository(string databasePath) : IWorkEntryRepository
{
    private readonly SqliteExecutor _executor = new(databasePath);

    public async Task<IReadOnlyList<WorkEntry>> GetByWeekAsync(WeekPeriod week)
    {
        var start = week.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endExclusive = week.EndExclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await _executor.ExecuteAsync(async database =>
        {
            var rows = await database.Ado.SqlQueryAsync<SqliteWorkEntryRow>(
                "SELECT id AS Id, work_date AS WorkDate, content AS Content, created_at_utc AS CreatedAtUtc, " +
                "updated_at_utc AS UpdatedAtUtc, confirmed_minutes AS ConfirmedMinutes " +
                "FROM work_entries WHERE work_date >= @start AND work_date < @end " +
                "ORDER BY work_date DESC, created_at_utc DESC",
                new { start, end = endExclusive });
            return rows.Select(ToEntry).ToArray();
        });
    }

    public async Task<WorkEntry?> FindAsync(Guid id)
    {
        var key = id.ToString("D");
        return await _executor.ExecuteAsync(async database =>
        {
            var rows = await database.Ado.SqlQueryAsync<SqliteWorkEntryRow>(
                "SELECT id AS Id, work_date AS WorkDate, content AS Content, created_at_utc AS CreatedAtUtc, " +
                "updated_at_utc AS UpdatedAtUtc, confirmed_minutes AS ConfirmedMinutes " +
                "FROM work_entries WHERE id = @key",
                new { key });
            var row = rows.FirstOrDefault();
            return row is null ? null : ToEntry(row);
        });
    }

    // [新增-离线存储] Phase 1 切片 F：写路径入队化——业务写入与 sync_operation 入队同一队列事务
    //（RunInOperationAsync 内部即 Serializable 事务），Application 服务零改动即自动产生可同步操作组。
    // [旧代码-离线存储] 原独立连接写法（无入队）
    // await _executor.ExecuteAsync(async database => await database.Ado.ExecuteCommandAsync(...));
    public async Task AddAsync(WorkEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        DbTextLimits.Validate(entry.Content, "工作内容");
        var id = entry.Id.ToString("D");
        var workDate = entry.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var createdAt = SqliteTimestamp.Format(entry.CreatedAtUtc);
        var updatedAt = SqliteTimestamp.Format(entry.UpdatedAtUtc);
        await new SqliteOperationQueue(databasePath).RunInOperationAsync("work-entry.save", async session =>
        {
            await session.Database.Ado.ExecuteCommandAsync(
                "INSERT INTO work_entries (id, work_date, content, created_at_utc, updated_at_utc, confirmed_minutes) " +
                "VALUES (@id, @workDate, @content, @createdAt, @updatedAt, @confirmedMinutes)",
                new { id, workDate, content = entry.Content, createdAt, updatedAt, confirmedMinutes = entry.ConfirmedMinutes });
            session.Record(new LocalOperationChange("work_entries", id, workDate, LocalObjectChange.Add)
            {
                AfterJson = SqliteOperationPayload.Serialize(entry)
            });
            return true;
        });
    }

    public async Task<bool> UpdateAsync(WorkEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        DbTextLimits.Validate(entry.Content, "工作内容");
        var id = entry.Id.ToString("D");
        var workDate = entry.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var updatedAt = SqliteTimestamp.Format(entry.UpdatedAtUtc);
        return await new SqliteOperationQueue(databasePath).RunInOperationAsync("work-entry.save", async session =>
        {
            var existingRows = await session.Database.Ado.SqlQueryAsync<SqliteWorkEntryRow>(
                "SELECT id AS Id, work_date AS WorkDate, content AS Content, created_at_utc AS CreatedAtUtc, " +
                "updated_at_utc AS UpdatedAtUtc, confirmed_minutes AS ConfirmedMinutes " +
                "FROM work_entries WHERE id = @id",
                new { id });
            var existing = existingRows.FirstOrDefault();
            var changed = await session.Database.Ado.ExecuteCommandAsync(
                "UPDATE work_entries SET work_date = @workDate, content = @content, " +
                "updated_at_utc = @updatedAt, confirmed_minutes = @confirmedMinutes WHERE id = @id",
                new { id, workDate, content = entry.Content, updatedAt, confirmedMinutes = entry.ConfirmedMinutes }) > 0;
            if (changed)
            {
                var previous = existing
                    ?? throw new InvalidOperationException("更新成功但未能读取工作记录的变更前快照。");
                session.Record(new LocalOperationChange("work_entries", id, workDate, LocalObjectChange.Update)
                {
                    BeforeJson = SqliteOperationPayload.Serialize(ToEntry(previous)),
                    AfterJson = SqliteOperationPayload.Serialize(entry)
                });
            }

            return changed;
        });
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var key = id.ToString("D");
        return await new SqliteOperationQueue(databasePath).RunInOperationAsync("work-entry.save", async session =>
        {
            // 删除意图需携带行数据（同步侧删除需要自然键）；行不存在则不入队
            var rows = await session.Database.Ado.SqlQueryAsync<SqliteWorkEntryRow>(
                "SELECT id AS Id, work_date AS WorkDate, content AS Content, created_at_utc AS CreatedAtUtc, " +
                "updated_at_utc AS UpdatedAtUtc, confirmed_minutes AS ConfirmedMinutes FROM work_entries WHERE id = @key",
                new { key });
            var row = rows.FirstOrDefault();
            if (row is null)
            {
                return false;
            }

            await session.Database.Ado.ExecuteCommandAsync(
                "DELETE FROM work_entries WHERE id = @key", new { key });
            session.Record(new LocalOperationChange("work_entries", key, row.WorkDate, LocalObjectChange.Delete)
            {
                BeforeJson = SqliteOperationPayload.Serialize(ToEntry(row))
            });
            return true;
        });
    }

    // [新增-离线存储] Phase 1 切片 E：全量列举（首次镜像数据源；与 MySQL 版同序：工作日期 + 主键）
    public async Task<IReadOnlyList<WorkEntry>> ListAllAsync() =>
        await _executor.ExecuteAsync<IReadOnlyList<WorkEntry>>(async database =>
        {
            var rows = await database.Ado.SqlQueryAsync<SqliteWorkEntryRow>(
                "SELECT id AS Id, work_date AS WorkDate, content AS Content, created_at_utc AS CreatedAtUtc, " +
                "updated_at_utc AS UpdatedAtUtc, confirmed_minutes AS ConfirmedMinutes " +
                "FROM work_entries ORDER BY work_date, id");
            return rows.Select(ToEntry).ToArray();
        });

    internal static WorkEntry ToEntry(SqliteWorkEntryRow row) => new(
        Guid.Parse(row.Id),
        DateOnly.ParseExact(row.WorkDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.Content,
        SqliteTimestamp.Parse(row.CreatedAtUtc),
        SqliteTimestamp.Parse(row.UpdatedAtUtc),
        row.ConfirmedMinutes);
}
