using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public class GitSyncServiceTests
{
    [Fact]
    public async Task Apply_RequiresUserConfirmation()
    {
        var store = new TestStore();
        var service = CreateService(new TestReader(), store);
        var sync = await service.PrepareAsync([BoundProject()], Day, Day, TimeZoneInfo.Utc);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(sync, false, false, false));
        Assert.False(store.Applied);
    }

    [Fact]
    public async Task Apply_RequiresExplicitAcceptanceOfShallowHistory()
    {
        var store = new TestStore();
        var service = CreateService(new TestReader { IsShallow = true }, store);
        var sync = await service.PrepareAsync([BoundProject()], Day, Day, TimeZoneInfo.Utc);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(sync, true, false, false));
        Assert.False(store.Applied);
        await service.ApplyAsync(sync, true, false, true);
        Assert.True(store.Applied);
    }

    [Fact]
    public async Task Prepare_RejectsUnboundProjects()
    {
        var service = CreateService(new TestReader(), new TestStore());
        var project = new GitProject(Guid.NewGuid(), "项目", null, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PrepareAsync([project], Day, Day, TimeZoneInfo.Utc));
    }

    [Fact]
    public async Task Prepare_RejectsDuplicateProjectSelection()
    {
        var service = CreateService(new TestReader(), new TestStore());
        var project = BoundProject();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.PrepareAsync([project, project], Day, Day, TimeZoneInfo.Utc));
    }

    private static readonly DateOnly Day = new(2026, 10, 30);

    private static GitProject BoundProject() =>
        new(Guid.NewGuid(), "项目", Path.GetTempPath(), GitAuthorField.Email, "test@example.invalid");

    private static GitSyncService CreateService(TestReader reader, TestStore store) =>
        new(reader, store, new GitSyncPlanner(new GitHourEstimator()));

    private sealed class TestReader : IGitCommitReader
    {
        public bool IsShallow { get; init; }

        public Task<GitReadResult> ReadAsync(GitRepositoryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitReadResult([], HasHead: true, IsShallow));
    }

    private sealed class TestStore : IGitSyncStore
    {
        public bool Applied { get; private set; }

        public Task<IReadOnlyList<GitWorkItemSnapshot>> LoadAllAsync() =>
            Task.FromResult<IReadOnlyList<GitWorkItemSnapshot>>([]);

        public Task<IReadOnlyList<GitReportWorkItem>> GetByWeekAsync(WeekPeriod week) =>
            Task.FromResult<IReadOnlyList<GitReportWorkItem>>([]);

        public Task<GitSyncApplyResult> ApplyAsync(PreparedGitSync sync)
        {
            Applied = true;
            return Task.FromResult(new GitSyncApplyResult(0, 0, 0, 0));
        }

        public Task<bool> UpdateWorkItemAsync(Guid id, string description, int? confirmedMinutes) =>
            throw new NotSupportedException();

        public Task<bool> SoftDeleteWorkItemAsync(Guid id) => throw new NotSupportedException();
        public Task<int> RecalculateDateAsync(DateOnly date) => throw new NotSupportedException();
    }
}
