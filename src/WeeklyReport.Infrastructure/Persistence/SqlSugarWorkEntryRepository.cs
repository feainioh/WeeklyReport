using SqlSugar;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Persistence;

public sealed class SqlSugarWorkEntryRepository(IDatabaseConfigurationStore configurationStore) : IWorkEntryRepository
{
    private readonly SqlSugarExecutor _executor = new(configurationStore);

    public async Task<IReadOnlyList<WorkEntry>> GetByWeekAsync(WeekPeriod week)
    {
        var start = week.Start.ToDateTime(TimeOnly.MinValue);
        var end = week.EndExclusive.ToDateTime(TimeOnly.MinValue);
        return await _executor.ExecuteAsync<IReadOnlyList<WorkEntry>>(async database =>
        {
            var rows = await database.Queryable<WorkEntryRow>()
                .Where(row => row.WorkDate >= start && row.WorkDate < end)
                .OrderBy(row => row.WorkDate, OrderByType.Desc)
                .OrderBy(row => row.CreatedAtUtc, OrderByType.Desc)
                .ToListAsync();
            return rows.Select(ToEntry).ToArray();
        });
    }

    public async Task<WorkEntry?> FindAsync(Guid id)
    {
        var key = id.ToString("D");
        return await _executor.ExecuteAsync(async database =>
        {
            var row = await database.Queryable<WorkEntryRow>().FirstAsync(item => item.Id == key);
            return row is null ? null : ToEntry(row);
        });
    }

    public async Task AddAsync(WorkEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        DbTextLimits.Validate(entry.Content, "工作内容");
        await _executor.ExecuteAsync(async database => await database.Insertable(ToRow(entry)).ExecuteCommandAsync());
    }

    public async Task<bool> UpdateAsync(WorkEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        DbTextLimits.Validate(entry.Content, "工作内容");
        return await _executor.ExecuteAsync(async database =>
            await database.Updateable(ToRow(entry))
                .UpdateColumns(row => new { row.WorkDate, row.Content, row.UpdatedAtUtc, row.ConfirmedMinutes })
                .ExecuteCommandAsync() > 0);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var key = id.ToString("D");
        return await _executor.ExecuteAsync(async database =>
            await database.Deleteable<WorkEntryRow>().Where(row => row.Id == key).ExecuteCommandAsync() > 0);
    }

    private static WorkEntryRow ToRow(WorkEntry entry) => new()
    {
        Id = entry.Id.ToString("D"),
        WorkDate = entry.WorkDate.ToDateTime(TimeOnly.MinValue),
        Content = entry.Content,
        CreatedAtUtc = entry.CreatedAtUtc.UtcDateTime,
        UpdatedAtUtc = entry.UpdatedAtUtc.UtcDateTime,
        ConfirmedMinutes = entry.ConfirmedMinutes
    };

    internal static WorkEntry ToEntry(WorkEntryRow row) => new(
        Guid.Parse(row.Id),
        DateOnly.FromDateTime(row.WorkDate),
        row.Content,
        new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc)),
        row.ConfirmedMinutes);
}
