using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public sealed class WeeklyReportOpenResult
{
    private WeeklyReportOpenResult(WeeklyReportSnapshot? existing, WeeklyReportDraft? draft)
    {
        Existing = existing;
        Draft = draft;
    }

    public WeeklyReportSnapshot? Existing { get; }
    public WeeklyReportDraft? Draft { get; }

    public static WeeklyReportOpenResult FromExisting(WeeklyReportSnapshot report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new WeeklyReportOpenResult(report, null);
    }

    public static WeeklyReportOpenResult FromDraft(WeeklyReportDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new WeeklyReportOpenResult(null, draft);
    }
}

public sealed class WeeklyReportService(
    IWeeklyReportRepository reports,
    IWorkEntryRepository workEntries,
    IGitReportWorkItemSource gitItems,
    WeeklyReportDraftBuilder builder,
    TimeProvider timeProvider)
{
    public async Task<WeeklyReportOpenResult> OpenOrCreateDraftAsync(DateOnly date)
    {
        var week = WeekPeriod.FromDate(date);
        var existing = await reports.FindByWeekAsync(week);
        if (existing is not null)
        {
            return WeeklyReportOpenResult.FromExisting(existing);
        }

        var manual = await workEntries.GetByWeekAsync(week);
        var git = await gitItems.GetByWeekAsync(week);
        return WeeklyReportOpenResult.FromDraft(builder.Build(week, manual, git));
    }

    public Task<IReadOnlyList<WeeklyReportSnapshot>> GetHistoryAsync() => reports.ListAsync();

    public async Task<WeeklyReportSnapshot> SaveNewAsync(
        WeeklyReportDraft draft,
        string workSummary,
        string nextWeekPlan,
        string risks)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (await reports.FindByWeekAsync(draft.Week) is not null)
        {
            throw new InvalidOperationException("该周周报已存在，请打开已有周报。");
        }

        var now = StorageDateRange.NormalizeUtcTimestamp(timeProvider.GetUtcNow());
        var report = new WeeklyReportSnapshot(
            Guid.NewGuid(), draft.Week, workSummary, nextWeekPlan, risks,
            draft.ConfirmedMinutes, draft.EstimatedMinutes, draft.UnknownHoursCount, now, now);
        await reports.AddAsync(report);
        return report;
    }

    public async Task<WeeklyReportSnapshot> UpdateAsync(
        WeeklyReportSnapshot existing,
        string workSummary,
        string nextWeekPlan,
        string risks)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var now = StorageDateRange.NormalizeUtcTimestamp(timeProvider.GetUtcNow());
        if (now < existing.UpdatedAtUtc)
        {
            throw new InvalidOperationException("系统时间早于周报更新时间，请检查系统时钟。");
        }

        if (now == existing.UpdatedAtUtc)
        {
            now = now.AddTicks(10);
        }

        var updated = new WeeklyReportSnapshot(
            existing.Id, existing.Week, workSummary, nextWeekPlan, risks,
            existing.ConfirmedMinutes, existing.EstimatedMinutes, existing.UnknownHoursCount,
            existing.CreatedAtUtc, now);
        if (!await reports.UpdateAsync(updated, existing.UpdatedAtUtc))
        {
            throw new KeyNotFoundException("周报不存在或已发生变化，请重新加载。");
        }

        return updated;
    }

    public async Task DeleteAsync(WeeklyReportSnapshot report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!await reports.DeleteAsync(report.Id, report.UpdatedAtUtc))
        {
            throw new KeyNotFoundException("周报不存在或已发生变化，请重新加载。");
        }
    }

}
