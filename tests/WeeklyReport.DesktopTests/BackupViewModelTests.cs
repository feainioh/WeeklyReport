using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using WeeklyReport.App.Services;
using WeeklyReport.App.ViewModels;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Backup;

namespace WeeklyReport.DesktopTests;

public class BackupViewModelTests
{
    [Fact]
    public async Task Refresh_DoesNotLockNavigationWhileLoading()
    {
        var reports = new FakeReports();
        var viewModel = CreateViewModel(new BlockingBackupStore(), reports);

        var refresh = viewModel.RefreshWeeksAsync();
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.IsNavigationLocked);

        reports.ListResult.SetResult([]);
        await refresh;
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsNavigationLocked);
    }

    [Fact]
    public async Task Refresh_PreservesSelectedWeeks()
    {
        var reports = new FakeReports();
        var first = CreateReport(new DateOnly(2026, 10, 19));
        var second = CreateReport(new DateOnly(2026, 10, 26));
        reports.ListResult.SetResult([first, second]);
        var viewModel = CreateViewModel(new BlockingBackupStore(), reports);
        await viewModel.RefreshWeeksAsync();
        viewModel.Weeks[0].IsSelected = true;

        await viewModel.RefreshWeeksAsync();

        Assert.True(viewModel.Weeks[0].IsSelected);
        Assert.False(viewModel.Weeks[1].IsSelected);
        Assert.True(viewModel.CanExportSelected);
        Assert.True(viewModel.CanExportSingle);
    }

    [Fact]
    public async Task Export_LocksNavigationAndReleasesItOnFailure()
    {
        var store = new BlockingBackupStore();
        var viewModel = CreateViewModel(store, new FakeReports());

        var export = viewModel.ExportAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"),
            BackupFileFormat.Json, overwrite: false);
        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.IsNavigationLocked);

        store.CaptureResult.SetException(new IOException("synthetic failure"));
        await export;
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsNavigationLocked);
        Assert.Contains("文件读写失败", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Import_LocksNavigationAndReleasesItOnFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WeeklyReport.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "backup.json");
        try
        {
            var files = new BackupFileService(new JsonBackupCodec(), new ExcelBackupCodec());
            var document = new BackupDocument(BackupDocument.CurrentFormatVersion, DateTimeOffset.UtcNow, [], [], [], []);
            await files.ExportAsync(path, BackupFileFormat.Json, document, overwrite: false);
            var store = new BlockingBackupStore();
            var viewModel = CreateViewModel(store, new FakeReports(), files);

            var import = viewModel.ImportAsync(path, BackupFileFormat.Json);
            Assert.True(viewModel.IsBusy);
            Assert.True(viewModel.IsNavigationLocked);

            store.CaptureResult.SetException(new IOException("synthetic failure"));
            Assert.False(await import);
            Assert.False(viewModel.IsBusy);
            Assert.False(viewModel.IsNavigationLocked);
            Assert.Contains("文件读写失败", viewModel.StatusMessage);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static BackupViewModel CreateViewModel(
        IBackupStore store, IWeeklyReportRepository reports, BackupFileService? files = null) =>
        new(new BackupService(store, new BackupImportPlanner(), TimeProvider.System),
            files ?? new BackupFileService(new JsonBackupCodec(), new ExcelBackupCodec()),
            reports, new ConfirmingDialogs(), new SkipReviews(), NullLogger<BackupViewModel>.Instance);

    private static WeeklyReportSnapshot CreateReport(DateOnly weekStart)
    {
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        return new WeeklyReportSnapshot(Guid.NewGuid(), new WeekPeriod(weekStart),
            "工作", "", "", 0, 0, 0, now, now);
    }

    private sealed class BlockingBackupStore : IBackupStore
    {
        public TaskCompletionSource<BackupDocument> CaptureResult { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<BackupDocument> CaptureAsync(DateTimeOffset exportedAtUtc) => CaptureResult.Task;
        public Task<BackupImportResult> ImportAsync(PreparedBackupImport import, BackupConflictPolicy policy) =>
            throw new NotSupportedException();
    }

    private sealed class FakeReports : IWeeklyReportRepository
    {
        public TaskCompletionSource<IReadOnlyList<WeeklyReportSnapshot>> ListResult { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<WeeklyReportSnapshot>> ListAsync() => ListResult.Task;
        public Task<WeeklyReportSnapshot?> FindByWeekAsync(WeekPeriod week) => throw new NotSupportedException();
        public Task AddAsync(WeeklyReportSnapshot report) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(WeeklyReportSnapshot report, DateTimeOffset expectedUpdatedAtUtc) =>
            throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, DateTimeOffset expectedUpdatedAtUtc) => throw new NotSupportedException();
    }

    private sealed class ConfirmingDialogs : IUserDialogs
    {
        public bool Confirm(string message, string title) => true;
        public void Error(string message, string title) => throw new InvalidOperationException(message);
    }

    private sealed class SkipReviews : IBackupReviewDialogs
    {
        public BackupConflictPolicy? ChooseImportPolicy(PreparedBackupImport import) => BackupConflictPolicy.Skip;
    }
}
