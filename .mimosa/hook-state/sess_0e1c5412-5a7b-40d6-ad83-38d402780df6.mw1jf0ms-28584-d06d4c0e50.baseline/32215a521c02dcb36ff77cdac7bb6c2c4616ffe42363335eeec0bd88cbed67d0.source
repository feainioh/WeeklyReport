using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public sealed class ScheduledGitSyncRunnerTests
{
    [Fact]
    public async Task RunAsync_SkipsWhenNoProjectIsBound()
    {
        var projects = new FakeProjects([]);
        var syncStore = new FakeSyncStore();
        var runner = CreateRunner(projects, new FakeReader(), syncStore);

        var outcome = await runner.RunAsync(
            ScheduledTaskKind.GitDailyItems, new DateOnly(2026, 11, 4));

        Assert.Equal(ScheduledTaskOutcomeKind.Skipped, outcome.Kind);
        Assert.False(syncStore.WasApplied);
    }

    [Fact]
    public async Task RunAsync_RejectsShallowHistoryWithoutApplying()
    {
        var project = CreateProject();
        var projects = new FakeProjects([project]);
        var reader = new FakeReader { IsShallow = true };
        var syncStore = new FakeSyncStore();
        var runner = CreateRunner(projects, reader, syncStore);

        var outcome = await runner.RunAsync(
            ScheduledTaskKind.GitDailyItems, new DateOnly(2026, 11, 4));

        Assert.Equal(ScheduledTaskOutcomeKind.Skipped, outcome.Kind);
        Assert.False(syncStore.WasApplied);
    }

    [Fact]
    public async Task RunAsync_AppliesWithDeletedConflictsSkipped()
    {
        var date = new DateOnly(2026, 11, 4);
        var project = CreateProject();
        var oldCommit = CreateCommit(project.Id, date, 'a');
        var incomingCommit = CreateCommit(project.Id, date, 'b');
        var projects = new FakeProjects([project]);
        var reader = new FakeReader { Commits = [incomingCommit] };
        var syncStore = new FakeSyncStore
        {
            ExistingItems =
            [
                new GitWorkItemSnapshot(
                    Guid.NewGuid(), project.Id, date, "已删除的工作项",
                    null, true, [oldCommit])
            ]
        };
        var runner = CreateRunner(projects, reader, syncStore);

        var outcome = await runner.RunAsync(ScheduledTaskKind.GitDailyItems, date);

        Assert.Equal(ScheduledTaskOutcomeKind.NoChanges, outcome.Kind);
        Assert.True(syncStore.WasApplied);
        Assert.Equal(1, syncStore.AppliedDeletedConflictCount);
    }

    private static ScheduledGitSyncRunner CreateRunner(
        FakeProjects projects,
        FakeReader reader,
        FakeSyncStore store)
    {
        var sync = new GitSyncService(reader, store, new GitSyncPlanner(new GitHourEstimator()));
        return new ScheduledGitSyncRunner(projects, sync);
    }

    private static GitProject CreateProject() =>
        new(Guid.NewGuid(), "项目", Path.GetFullPath(Path.Combine(Path.GetTempPath(), "scheduled-git-test")),
            GitAuthorField.Email, "dev@example.com");

    private static GitCommit CreateCommit(Guid projectId, DateOnly date, char hashCharacter) =>
        new(projectId, new string(hashCharacter, 40), "开发者", "dev@example.com",
            new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero), "完成任务");

    private sealed class FakeProjects(IReadOnlyList<GitProject> items) : IGitProjectRepository
    {
        public Task<IReadOnlyList<GitProject>> ListAsync() => Task.FromResult(items);
        public Task<GitProject?> FindAsync(Guid id) => Task.FromResult<GitProject?>(null);
        public Task AddAsync(GitProject project) => Task.CompletedTask;
        public Task<bool> UpdateAsync(GitProject project) => Task.FromResult(false);
        public Task<bool> DeleteAsync(Guid id) => Task.FromResult(false);
    }

    private sealed class FakeReader : IGitCommitReader
    {
        public IReadOnlyList<GitCommit> Commits { get; init; } = [];
        public bool IsShallow { get; init; }

        public Task<GitReadResult> ReadAsync(
            GitRepositoryQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitReadResult(Commits, HasHead: true, IsShallow));
    }

    private sealed class FakeSyncStore : IGitSyncStore
    {
        public IReadOnlyList<GitWorkItemSnapshot> ExistingItems { get; init; } = [];
        public bool WasApplied { get; private set; }
        public int AppliedDeletedConflictCount { get; private set; }

        public Task<IReadOnlyList<GitWorkItemSnapshot>> LoadAllAsync() =>
            Task.FromResult(ExistingItems);

        public Task<GitSyncApplyResult> ApplyAsync(PreparedGitSync sync)
        {
            WasApplied = true;
            AppliedDeletedConflictCount = sync.Preview.DeletedConflicts.Count;
            return Task.FromResult(new GitSyncApplyResult(0, 0, 0, AppliedDeletedConflictCount));
        }

        public Task<IReadOnlyList<GitReportWorkItem>> GetByWeekAsync(WeekPeriod week) =>
            Task.FromResult<IReadOnlyList<GitReportWorkItem>>([]);

        public Task<bool> UpdateWorkItemAsync(Guid id, string description, int? confirmedMinutes) =>
            Task.FromResult(false);

        public Task<bool> SoftDeleteWorkItemAsync(Guid id) => Task.FromResult(false);
        public Task<int> RecalculateDateAsync(DateOnly date) => Task.FromResult(0);
    }
}
