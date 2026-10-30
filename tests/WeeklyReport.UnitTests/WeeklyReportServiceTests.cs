using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public class WeeklyReportServiceTests
{
    private static readonly WeekPeriod Week = new(new DateOnly(2026, 10, 26));
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OpenOrCreateDraft_ReturnsExistingReportWithoutRegenerating()
    {
        var reports = new MemoryReports();
        var existing = Report("人工编辑");
        await reports.AddAsync(existing);
        var service = CreateService(reports, new FailingWorkSource(), new FailingGitSource());

        var result = await service.OpenOrCreateDraftAsync(Week.Start);

        Assert.Same(existing, result.Existing);
        Assert.Null(result.Draft);
        Assert.Equal("人工编辑", result.Existing!.WorkSummary);
    }

    [Fact]
    public async Task SaveNew_RejectsAnExistingWeek()
    {
        var reports = new MemoryReports();
        await reports.AddAsync(Report("已有周报"));
        var service = CreateService(reports, new FailingWorkSource(), new FailingGitSource());
        var draft = new WeeklyReportDraft(Week, "新草稿", 0, 0, 0);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveNewAsync(draft, "新内容", "", ""));

        Assert.Single(await reports.ListAsync());
        Assert.Equal("已有周报", (await reports.FindByWeekAsync(Week))!.WorkSummary);
    }

    [Fact]
    public async Task Update_PreservesSavedHourSnapshot()
    {
        var reports = new MemoryReports();
        var original = Report("原内容");
        await reports.AddAsync(original);
        var service = CreateService(reports, new FailingWorkSource(), new FailingGitSource());

        var updated = await service.UpdateAsync(original, "修改后的内容", "计划", "风险");

        Assert.Equal(original.ConfirmedMinutes, updated.ConfirmedMinutes);
        Assert.Equal(original.EstimatedMinutes, updated.EstimatedMinutes);
        Assert.Equal(original.UnknownHoursCount, updated.UnknownHoursCount);
        Assert.Equal(original.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal("修改后的内容", updated.WorkSummary);
    }

    private static WeeklyReportService CreateService(
        IWeeklyReportRepository reports, IWorkEntryRepository workEntries, IGitReportWorkItemSource gitItems) =>
        new(reports, workEntries, gitItems, new WeeklyReportDraftBuilder(), new FixedClock(Now.AddHours(1)));

    private static WeeklyReportSnapshot Report(string summary) =>
        new(Guid.NewGuid(), Week, summary, "", "", 90, 45, 1, Now, Now);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FailingWorkSource : IWorkEntryRepository
    {
        public Task<IReadOnlyList<WorkEntry>> GetByWeekAsync(WeekPeriod week) => throw new InvalidOperationException();
        public Task<WorkEntry?> FindAsync(Guid id) => throw new NotSupportedException();
        public Task AddAsync(WorkEntry entry) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(WorkEntry entry) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class FailingGitSource : IGitReportWorkItemSource
    {
        public Task<IReadOnlyList<GitReportWorkItem>> GetByWeekAsync(WeekPeriod week) => throw new InvalidOperationException();
    }

    private sealed class MemoryReports : IWeeklyReportRepository
    {
        private readonly Dictionary<Guid, WeeklyReportSnapshot> _reports = new();

        public Task<WeeklyReportSnapshot?> FindByWeekAsync(WeekPeriod week) =>
            Task.FromResult(_reports.Values.SingleOrDefault(report => report.Week == week));

        public Task<IReadOnlyList<WeeklyReportSnapshot>> ListAsync() =>
            Task.FromResult<IReadOnlyList<WeeklyReportSnapshot>>(_reports.Values.ToArray());

        public Task AddAsync(WeeklyReportSnapshot report)
        {
            _reports.Add(report.Id, report);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(WeeklyReportSnapshot report, DateTimeOffset expectedUpdatedAtUtc)
        {
            if (!_reports.TryGetValue(report.Id, out var existing) || existing.UpdatedAtUtc != expectedUpdatedAtUtc)
            {
                return Task.FromResult(false);
            }

            _reports[report.Id] = report;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(Guid id, DateTimeOffset expectedUpdatedAtUtc)
        {
            if (!_reports.TryGetValue(id, out var existing) || existing.UpdatedAtUtc != expectedUpdatedAtUtc)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(_reports.Remove(id));
        }
    }
}
