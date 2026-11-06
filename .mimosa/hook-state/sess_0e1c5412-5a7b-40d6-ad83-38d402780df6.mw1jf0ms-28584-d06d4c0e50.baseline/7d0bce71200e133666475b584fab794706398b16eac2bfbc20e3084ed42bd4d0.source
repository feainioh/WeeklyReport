using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using WeeklyReport.App.Services;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.DesktopTests;

public sealed class ScheduledTaskMonitorSimulationTests
{
    [Fact]
    public async Task ScheduledWeeklyDraft_RunsAtConfiguredTime_PersistsResult_AndDoesNotRepeat()
    {
        using var directory = new TemporaryDirectory();
        var date = new DateOnly(2026, 11, 4);
        var clock = new ManualTimeProvider(date.ToDateTime(new TimeOnly(15, 59)));
        var settingsStore = new JsonScheduledTaskSettingsStore(
            directory.FilePath, NullLogger<JsonScheduledTaskSettingsStore>.Instance);
        await settingsStore.SaveAsync(ScheduledTaskSettings.Default with
        {
            WeeklyDraft = new ScheduledTaskOptions(
                true, new TimeOnly(16, 0), DayOfWeek.Wednesday)
        });

        var reports = new FakeReports();
        var entries = new FakeEntries(
        [
            new WorkEntry(
                Guid.NewGuid(), date, "完成本地定时任务模拟",
                StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow),
                StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow),
                confirmedMinutes: 45)
        ]);
        var weeklyReportService = new WeeklyReportService(
            reports, entries, new EmptyGitItems(), new WeeklyReportDraftBuilder(), clock);
        var outcomes = new List<(ScheduledTaskKind Kind, ScheduledTaskOutcome Outcome)>();
        await using var monitor = new ScheduledTaskMonitor(
            settingsStore,
            new ScheduledTaskScheduler(),
            new ScheduledGitSyncRunner(new EmptyProjects(), new GitSyncService(
                new EmptyGitReader(), new EmptySyncStore(), new GitSyncPlanner(new GitHourEstimator()))),
            new ScheduledWeeklyDraftRunner(weeklyReportService),
            clock,
            (kind, outcome) => outcomes.Add((kind, outcome)),
            NullLogger<ScheduledTaskMonitor>.Instance);

        await monitor.InitializeAsync();
        monitor.Start();
        clock.Advance(TimeSpan.FromMinutes(1));
        await monitor.CheckDueTasksAsync();

        Assert.NotNull(reports.Added);
        Assert.Contains("完成本地定时任务模拟", reports.Added.WorkSummary);
        Assert.Equal(
            (ScheduledTaskKind.WeeklyDraft, ScheduledTaskOutcomeKind.Success),
            (Assert.Single(outcomes).Kind, outcomes[0].Outcome.Kind));
        Assert.Equal(date, monitor.Settings.WeeklyDraft.LastAttemptDate);
        Assert.Equal(date, monitor.Settings.WeeklyDraft.LastSuccessfulDate);

        await monitor.CheckDueTasksAsync();

        Assert.Single(outcomes);
        Assert.NotNull(await settingsStore.LoadAsync());
        Assert.Equal(date, (await settingsStore.LoadAsync()).WeeklyDraft.LastAttemptDate);
    }

    [Fact]
    public async Task MissedScheduledTimeAfterMonitorStarts_IsSkippedWithoutExecutingRunner()
    {
        using var directory = new TemporaryDirectory();
        var date = new DateOnly(2026, 11, 4);
        var clock = new ManualTimeProvider(date.ToDateTime(new TimeOnly(15, 59)));
        var settingsStore = new JsonScheduledTaskSettingsStore(
            directory.FilePath, NullLogger<JsonScheduledTaskSettingsStore>.Instance);
        await settingsStore.SaveAsync(ScheduledTaskSettings.Default with
        {
            WeeklyDraft = new ScheduledTaskOptions(
                true, new TimeOnly(16, 0), DayOfWeek.Wednesday)
        });
        var reports = new FakeReports();
        var outcomes = new List<ScheduledTaskOutcome>();
        var weeklyReportService = new WeeklyReportService(
            reports, new FakeEntries([]), new EmptyGitItems(),
            new WeeklyReportDraftBuilder(), clock);
        await using var monitor = new ScheduledTaskMonitor(
            settingsStore,
            new ScheduledTaskScheduler(),
            new ScheduledGitSyncRunner(new EmptyProjects(), new GitSyncService(
                new EmptyGitReader(), new EmptySyncStore(), new GitSyncPlanner(new GitHourEstimator()))),
            new ScheduledWeeklyDraftRunner(weeklyReportService),
            clock,
            (_, outcome) => outcomes.Add(outcome),
            NullLogger<ScheduledTaskMonitor>.Instance);
        await monitor.InitializeAsync();
        monitor.Start();

        clock.Advance(TimeSpan.FromMinutes(3));
        await monitor.CheckDueTasksAsync();

        Assert.Equal(ScheduledTaskOutcomeKind.Skipped, Assert.Single(outcomes).Kind);
        Assert.Null(reports.Added);
        Assert.Equal(date, monitor.Settings.WeeklyDraft.LastAttemptDate);
        Assert.Null(monitor.Settings.WeeklyDraft.LastSuccessfulDate);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTime localNow)
        {
            localNow = DateTime.SpecifyKind(localNow, DateTimeKind.Unspecified);
            _utcNow = new DateTimeOffset(localNow, TimeZoneInfo.Local.GetUtcOffset(localNow)).ToUniversalTime();
        }

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"scheduled-monitor-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(_path);
        public string FilePath => Path.Combine(_path, "settings.json");

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
    }

    private sealed class FakeReports : IWeeklyReportRepository
    {
        public WeeklyReportSnapshot? Added { get; private set; }

        public Task<WeeklyReportSnapshot?> FindByWeekAsync(WeekPeriod week) =>
            Task.FromResult<WeeklyReportSnapshot?>(Added?.Week == week ? Added : null);

        public Task<IReadOnlyList<WeeklyReportSnapshot>> ListAsync() =>
            Task.FromResult<IReadOnlyList<WeeklyReportSnapshot>>([]);

        public Task AddAsync(WeeklyReportSnapshot report)
        {
            Added = report;
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(WeeklyReportSnapshot report, DateTimeOffset expectedUpdatedAtUtc) =>
            Task.FromResult(false);

        public Task<bool> DeleteAsync(Guid id, DateTimeOffset expectedUpdatedAtUtc) =>
            Task.FromResult(false);
    }

    private sealed class FakeEntries(IReadOnlyList<WorkEntry> items) : IWorkEntryRepository
    {
        public Task<IReadOnlyList<WorkEntry>> GetByWeekAsync(WeekPeriod week) =>
            Task.FromResult<IReadOnlyList<WorkEntry>>(
                items.Where(item => week.Contains(item.WorkDate)).ToArray());

        public Task<WorkEntry?> FindAsync(Guid id) => Task.FromResult<WorkEntry?>(null);
        public Task AddAsync(WorkEntry entry) => Task.CompletedTask;
        public Task<bool> UpdateAsync(WorkEntry entry) => Task.FromResult(false);
        public Task<bool> DeleteAsync(Guid id) => Task.FromResult(false);
        public Task<IReadOnlyList<WorkEntry>> ListAllAsync() => Task.FromResult(items);
    }

    private sealed class EmptyGitItems : IGitReportWorkItemSource
    {
        public Task<IReadOnlyList<GitReportWorkItem>> GetByWeekAsync(WeekPeriod week) =>
            Task.FromResult<IReadOnlyList<GitReportWorkItem>>([]);
    }

    private sealed class EmptyProjects : IGitProjectRepository
    {
        public Task<IReadOnlyList<GitProject>> ListAsync() =>
            Task.FromResult<IReadOnlyList<GitProject>>([]);
        public Task<GitProject?> FindAsync(Guid id) => Task.FromResult<GitProject?>(null);
        public Task AddAsync(GitProject project) => Task.CompletedTask;
        public Task<bool> UpdateAsync(GitProject project) => Task.FromResult(false);
        public Task<bool> DeleteAsync(Guid id) => Task.FromResult(false);
    }

    private sealed class EmptyGitReader : IGitCommitReader
    {
        public Task<GitReadResult> ReadAsync(
            GitRepositoryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitReadResult([], false, false));
    }

    private sealed class EmptySyncStore : IGitSyncStore
    {
        public Task<IReadOnlyList<GitWorkItemSnapshot>> LoadAllAsync() =>
            Task.FromResult<IReadOnlyList<GitWorkItemSnapshot>>([]);
        public Task<GitSyncApplyResult> ApplyAsync(PreparedGitSync sync) =>
            Task.FromResult(new GitSyncApplyResult(0, 0, 0, 0));
        public Task<IReadOnlyList<GitReportWorkItem>> GetByWeekAsync(WeekPeriod week) =>
            Task.FromResult<IReadOnlyList<GitReportWorkItem>>([]);
        public Task<bool> UpdateWorkItemAsync(Guid id, string description, int? confirmedMinutes) =>
            Task.FromResult(false);
        public Task<bool> SoftDeleteWorkItemAsync(Guid id) => Task.FromResult(false);
        public Task<int> RecalculateDateAsync(DateOnly date) => Task.FromResult(0);
    }
}
