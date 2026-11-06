using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using WeeklyReport.App.Services;
using WeeklyReport.App.ViewModels;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.DesktopTests;

public sealed class NavigationResultTests
{
    [Fact]
    public async Task WorkEntryNavigation_WhenLoadingSucceeds_ReturnsTrue()
    {
        var viewModel = new WorkEntriesViewModel(
            new WorkEntryService(new FakeWorkEntries(), TimeProvider.System),
            new EmptyGitItems(),
            new ConfirmingDialogs(),
            NullLogger<WorkEntriesViewModel>.Instance);

        var result = await viewModel.NavigateToWeekAsync(new DateOnly(2026, 11, 2));

        Assert.True(result);
    }

    [Fact]
    public async Task WorkEntryNavigation_WhenLoadingFails_ReturnsFalse()
    {
        var viewModel = new WorkEntriesViewModel(
            new WorkEntryService(new FakeWorkEntries(throwOnLoad: true), TimeProvider.System),
            new EmptyGitItems(),
            new ConfirmingDialogs(),
            NullLogger<WorkEntriesViewModel>.Instance);

        var result = await viewModel.NavigateToWeekAsync(new DateOnly(2026, 11, 2));

        Assert.False(result);
        Assert.NotEqual(new DateOnly(2026, 11, 2),
            DateOnly.FromDateTime(viewModel.SelectedWeek!.Value));
        Assert.Contains("工作记录加载失败", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitWorkItemNavigation_WhenLoadingSucceeds_ReturnsTrue()
    {
        var viewModel = CreateGitProjectsViewModel(new FakeGitSyncStore(throwOnLoad: false));

        var result = await viewModel.NavigateToWorkWeekAsync(new DateOnly(2026, 11, 2));

        Assert.True(result);
    }

    [Fact]
    public async Task GitWorkItemNavigation_WhenLoadingFails_ReturnsFalse()
    {
        var viewModel = CreateGitProjectsViewModel(new FakeGitSyncStore(throwOnLoad: true));

        var result = await viewModel.NavigateToWorkWeekAsync(new DateOnly(2026, 11, 2));

        Assert.False(result);
        Assert.NotEqual(new DateOnly(2026, 11, 2),
            DateOnly.FromDateTime(viewModel.WorkWeek!.Value));
        Assert.Contains("Git 工作项加载失败", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    private static GitProjectsViewModel CreateGitProjectsViewModel(FakeGitSyncStore store) =>
        new(new EmptyProjects(),
            store,
            new GitSyncService(new UnusedGitReader(), new EmptyGitSyncStore(), new GitSyncPlanner(new GitHourEstimator())),
            new WorkEntryService(new FakeWorkEntries(), TimeProvider.System),
            new AiDraftCoordinator(new UnusedAiSettings(), new UnusedAiDraft(), new UnusedAiReviewDialogs()),
            new ConfirmingDialogs(),
            NullLogger<GitProjectsViewModel>.Instance);

    private sealed class FakeWorkEntries(bool throwOnLoad = false) : IWorkEntryRepository
    {
        public Task<IReadOnlyList<WorkEntry>> GetByWeekAsync(WeekPeriod week) =>
            throwOnLoad
                ? Task.FromException<IReadOnlyList<WorkEntry>>(
                    new DatabaseAccessException("工作记录加载失败", new IOException()))
                : Task.FromResult<IReadOnlyList<WorkEntry>>([]);

        public Task<WorkEntry?> FindAsync(Guid id) => throw new NotSupportedException();
        public Task AddAsync(WorkEntry entry) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(WorkEntry entry) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkEntry>> ListAllAsync() => throw new NotSupportedException();
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

        public Task<GitProject?> FindAsync(Guid id) => throw new NotSupportedException();
        public Task AddAsync(GitProject project) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(GitProject project) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class EmptyGitSyncStore : IGitSyncStore
    {
        public Task<IReadOnlyList<GitWorkItemSnapshot>> LoadAllAsync() =>
            Task.FromResult<IReadOnlyList<GitWorkItemSnapshot>>([]);

        public Task<IReadOnlyList<GitReportWorkItem>> GetByWeekAsync(WeekPeriod week) =>
            Task.FromResult<IReadOnlyList<GitReportWorkItem>>([]);

        public Task<GitSyncApplyResult> ApplyAsync(PreparedGitSync sync) => throw new NotSupportedException();
        public Task<bool> UpdateWorkItemAsync(Guid id, string description, int? confirmedMinutes) =>
            throw new NotSupportedException();
        public Task<bool> SoftDeleteWorkItemAsync(Guid id) => throw new NotSupportedException();
        public Task<int> RecalculateDateAsync(DateOnly date) => throw new NotSupportedException();
    }

    private sealed class FakeGitSyncStore(bool throwOnLoad) : IGitSyncStore
    {
        public Task<IReadOnlyList<GitReportWorkItem>> GetByWeekAsync(WeekPeriod week) =>
            throwOnLoad
                ? Task.FromException<IReadOnlyList<GitReportWorkItem>>(
                    new DatabaseAccessException("Git 工作项加载失败", new IOException()))
                : Task.FromResult<IReadOnlyList<GitReportWorkItem>>([]);

        public Task<IReadOnlyList<GitWorkItemSnapshot>> LoadAllAsync() =>
            Task.FromResult<IReadOnlyList<GitWorkItemSnapshot>>([]);

        public Task<GitSyncApplyResult> ApplyAsync(PreparedGitSync sync) => throw new NotSupportedException();
        public Task<bool> UpdateWorkItemAsync(Guid id, string description, int? confirmedMinutes) =>
            throw new NotSupportedException();
        public Task<bool> SoftDeleteWorkItemAsync(Guid id) => throw new NotSupportedException();
        public Task<int> RecalculateDateAsync(DateOnly date) => throw new NotSupportedException();
    }

    private sealed class UnusedGitReader : IGitCommitReader
    {
        public Task<GitReadResult> ReadAsync(
            GitRepositoryQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ConfirmingDialogs : IUserDialogs
    {
        public bool Confirm(string message, string title) => true;
        public void Error(string message, string title) => throw new InvalidOperationException(message);
    }

    private sealed class UnusedAiSettings : IAiSettingsStore
    {
        public Task<AiSettings?> LoadAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(AiSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedAiDraft : IAiDraftService
    {
        public Task<string> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedAiReviewDialogs : IAiReviewDialogs
    {
        public bool ConfirmSend(string model, string instruction, string content) =>
            throw new NotSupportedException();

        public bool ConfirmApply(string result) => throw new NotSupportedException();
    }
}
