using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public sealed class WorkEntryService(IWorkEntryRepository repository, TimeProvider timeProvider)
{
    public Task<IReadOnlyList<WorkEntry>> GetByWeekAsync(DateOnly date) =>
        repository.GetByWeekAsync(WeekPeriod.FromDate(date));

    public async Task<WorkEntry> CreateAsync(DateOnly date, string content, int? confirmedMinutes = null)
    {
        var now = StorageDateRange.NormalizeUtcTimestamp(timeProvider.GetUtcNow());
        var entry = new WorkEntry(Guid.NewGuid(), date, content, now, now, confirmedMinutes);
        await repository.AddAsync(entry);
        return entry;
    }

    public async Task<WorkEntry> UpdateAsync(Guid id, DateOnly date, string content, int? confirmedMinutes)
    {
        var existing = await repository.FindAsync(id)
            ?? throw new KeyNotFoundException("工作记录不存在，可能已被删除。");
        var now = StorageDateRange.NormalizeUtcTimestamp(timeProvider.GetUtcNow());
        if (now < existing.UpdatedAtUtc)
        {
            throw new InvalidOperationException("系统时间早于记录更新时间，请检查系统时钟。");
        }

        if (now == existing.UpdatedAtUtc)
        {
            now = now.AddTicks(10);
        }

        var entry = new WorkEntry(id, date, content, existing.CreatedAtUtc, now, confirmedMinutes);
        if (!await repository.UpdateAsync(entry))
        {
            throw new KeyNotFoundException("工作记录不存在，可能已被删除。");
        }

        return entry;
    }

    public async Task DeleteAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("工作记录标识不能为空。", nameof(id));
        }

        if (!await repository.DeleteAsync(id))
        {
            throw new KeyNotFoundException("工作记录不存在，可能已被删除。");
        }
    }
}
