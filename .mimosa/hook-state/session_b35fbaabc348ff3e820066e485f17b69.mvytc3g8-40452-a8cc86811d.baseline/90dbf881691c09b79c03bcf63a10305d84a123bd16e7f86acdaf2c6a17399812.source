using Microsoft.Extensions.Logging.Abstractions;
using WeeklyReport.App.Services;
using WeeklyReport.App.ViewModels;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.DesktopTests;

public class ReminderNavigationTests
{
    [Fact]
    public async Task OpenWeek_WaitsForCurrentOperationBeforeOpening()
    {
        var reports = new FakeReports();
        var viewModel = CreateViewModel(reports, new FakeDialogs());
        var refresh = viewModel.RefreshHistoryAsync();
        Assert.True(viewModel.IsBusy);
        var week = new DateOnly(2026, 10, 26);

        var open = viewModel.OpenWeekAsync(week);
        Assert.False(open.IsCompleted);
        Assert.Contains("当前操作完成后", viewModel.StatusMessage);

        reports.ListResult.SetResult([]);
        await refresh;
        await open;

        Assert.Equal(week.ToDateTime(TimeOnly.MinValue), viewModel.SelectedDate);
        Assert.True(viewModel.HasReport);
        Assert.Contains("2026-10-26", viewModel.EditorTitle);
    }

    [Fact]
    public async Task OpenWeek_RespectsUnsavedChangesWhenUserDeclines()
    {
        var reports = new FakeReports();
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var viewModel = CreateViewModel(reports, dialogs);
        var firstWeek = new DateOnly(2026, 10, 19);
        await viewModel.OpenWeekAsync(firstWeek);
        viewModel.WorkSummary = "未保存的内容";

        await viewModel.OpenWeekAsync(new DateOnly(2026, 10, 26));

        Assert.Equal("未保存的内容", viewModel.WorkSummary);
        Assert.Equal(firstWeek.ToDateTime(TimeOnly.MinValue), viewModel.SelectedDate);
        Assert.True(viewModel.IsDirty);
        Assert.Equal(1, dialogs.ConfirmCount);
    }

    private static WeeklyReportsViewModel CreateViewModel(IWeeklyReportRepository reports, IUserDialogs dialogs)
    {
        var service = new WeeklyReportService(
            reports, new EmptyEntries(), new EmptyGitItems(), new WeeklyReportDraftBuilder(), TimeProvider.System);
        var ai = new AiDraftCoordinator(new UnusedAiSettings(), new UnusedAiClient(), new UnusedAiDialogs());
        return new WeeklyReportsViewModel(service, ai, dialogs, NullLogger<WeeklyReportsViewModel>.Instance);
    }

    private sealed class FakeReports : IWeeklyReportRepository
    {
        public TaskCompletionSource<IReadOnlyList<WeeklyReportSnapshot>> ListResult { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WeeklyReportSnapshot?> FindByWeekAsync(WeekPeriod week) =>
            Task.FromResult<WeeklyReportSnapshot?>(null);
        public Task<IReadOnlyList<WeeklyReportSnapshot>> ListAsync() => ListResult.Task;
        public Task AddAsync(WeeklyReportSnapshot report) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(WeeklyReportSnapshot report, DateTimeOffset expectedUpdatedAtUtc) =>
            throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, DateTimeOffset expectedUpdatedAtUtc) => throw new NotSupportedException();
    }

    private sealed class EmptyEntries : IWorkEntryRepository
    {
        public Task<IReadOnlyList<WorkEntry>> GetByWeekAsync(WeekPeriod week) =>
            Task.FromResult<IReadOnlyList<WorkEntry>>([]);
        public Task<WorkEntry?> FindAsync(Guid id) => throw new NotSupportedException();
        public Task AddAsync(WorkEntry entry) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(WorkEntry entry) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class EmptyGitItems : IGitReportWorkItemSource
    {
        public Task<IReadOnlyList<GitReportWorkItem>> GetByWeekAsync(WeekPeriod week) =>
            Task.FromResult<IReadOnlyList<GitReportWorkItem>>([]);
    }

    private sealed class FakeDialogs : IUserDialogs
    {
        public bool ConfirmResult { get; init; }
        public int ConfirmCount { get; private set; }
        public bool Confirm(string message, string title)
        {
            ConfirmCount++;
            return ConfirmResult;
        }

        public void Error(string message, string title) => throw new InvalidOperationException(message);
    }

    private sealed class UnusedAiSettings : IAiSettingsStore
    {
        public Task<AiSettings?> LoadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveAsync(AiSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedAiClient : IAiDraftService
    {
        public Task<string> GenerateAsync(AiGenerationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedAiDialogs : IAiReviewDialogs
    {
        public bool ConfirmSend(string model, string instruction, string content) => throw new NotSupportedException();
        public bool ConfirmApply(string result) => throw new NotSupportedException();
    }
}
