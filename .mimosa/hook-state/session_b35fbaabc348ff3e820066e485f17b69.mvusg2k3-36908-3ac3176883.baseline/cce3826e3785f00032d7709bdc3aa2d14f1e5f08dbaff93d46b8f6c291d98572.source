using SqlSugar;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Persistence;

public sealed class SqlSugarWeeklyReportRepository(IDatabaseConfigurationStore configurationStore) : IWeeklyReportRepository
{
    private readonly SqlSugarExecutor _executor = new(configurationStore);

    public async Task<WeeklyReportSnapshot?> FindByWeekAsync(WeekPeriod week)
    {
        var start = week.Start.ToDateTime(TimeOnly.MinValue);
        return await _executor.ExecuteAsync(async database =>
        {
            var row = await database.Queryable<WeeklyReportRow>().FirstAsync(item => item.WeekStart == start);
            return row is null ? null : ToReport(row);
        });
    }

    public async Task<IReadOnlyList<WeeklyReportSnapshot>> ListAsync() =>
        await _executor.ExecuteAsync<IReadOnlyList<WeeklyReportSnapshot>>(async database =>
        {
            var rows = await database.Queryable<WeeklyReportRow>()
                .OrderBy(row => row.WeekStart, OrderByType.Desc)
                .ToListAsync();
            return rows.Select(ToReport).ToArray();
        });

    public async Task AddAsync(WeeklyReportSnapshot report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateText(report);
        await _executor.ExecuteAsync(async database => await database.Insertable(ToRow(report)).ExecuteCommandAsync());
    }

    public async Task<bool> UpdateAsync(WeeklyReportSnapshot report, DateTimeOffset expectedUpdatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateText(report);
        var id = report.Id.ToString("D");
        var expected = expectedUpdatedAtUtc.UtcDateTime;
        return await _executor.ExecuteAsync(async database =>
            await database.Updateable(ToRow(report))
                .UpdateColumns(row => new { row.WorkSummary, row.NextWeekPlan, row.Risks, row.UpdatedAtUtc })
                .Where(row => row.Id == id && row.UpdatedAtUtc == expected)
                .ExecuteCommandAsync() > 0);
    }

    public async Task<bool> DeleteAsync(Guid id, DateTimeOffset expectedUpdatedAtUtc)
    {
        var key = id.ToString("D");
        var expected = expectedUpdatedAtUtc.UtcDateTime;
        return await _executor.ExecuteAsync(async database =>
            await database.Deleteable<WeeklyReportRow>()
                .Where(row => row.Id == key && row.UpdatedAtUtc == expected)
                .ExecuteCommandAsync() > 0);
    }

    private static WeeklyReportRow ToRow(WeeklyReportSnapshot report) => new()
    {
        Id = report.Id.ToString("D"),
        WeekStart = report.Week.Start.ToDateTime(TimeOnly.MinValue),
        WeekEnd = report.Week.EndInclusive.ToDateTime(TimeOnly.MinValue),
        WorkSummary = report.WorkSummary,
        NextWeekPlan = report.NextWeekPlan,
        Risks = report.Risks,
        ConfirmedMinutes = report.ConfirmedMinutes,
        EstimatedMinutes = report.EstimatedMinutes,
        UnknownHoursCount = report.UnknownHoursCount,
        CreatedAtUtc = report.CreatedAtUtc.UtcDateTime,
        UpdatedAtUtc = report.UpdatedAtUtc.UtcDateTime
    };

    private static void ValidateText(WeeklyReportSnapshot report)
    {
        DbTextLimits.Validate(report.WorkSummary, "本周工作");
        DbTextLimits.Validate(report.NextWeekPlan, "下周计划");
        DbTextLimits.Validate(report.Risks, "问题与风险");
    }

    internal static WeeklyReportSnapshot ToReport(WeeklyReportRow row)
    {
        var week = new WeekPeriod(DateOnly.FromDateTime(row.WeekStart));
        if (DateOnly.FromDateTime(row.WeekEnd) != week.EndInclusive)
        {
            throw new InvalidDataException("周报的起止日期不一致，请检查数据库。");
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
            new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc)));
    }
}
