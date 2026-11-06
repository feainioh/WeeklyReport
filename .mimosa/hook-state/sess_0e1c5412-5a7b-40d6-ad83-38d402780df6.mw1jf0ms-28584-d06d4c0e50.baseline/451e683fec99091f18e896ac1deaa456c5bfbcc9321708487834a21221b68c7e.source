using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WeeklyReport.App.Services;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Backup;

namespace WeeklyReport.App.ViewModels;

public sealed partial class BackupWeekItem(WeeklyReportSnapshot report) : ObservableObject
{
    public WeeklyReportSnapshot Report { get; } = report;
    public string Label => $"{Report.Week.Start:yyyy-MM-dd} 至 {Report.Week.EndInclusive:yyyy-MM-dd}";

    [ObservableProperty]
    private bool _isSelected;
}

public partial class BackupViewModel(
    BackupService service,
    BackupFileService files,
    IWeeklyReportRepository reports,
    IUserDialogs dialogs,
    IBackupReviewDialogs reviews,
    ILogger<BackupViewModel> logger) : ObservableObject
{
    public ObservableCollection<BackupWeekItem> Weeks { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isNavigationLocked;
    [ObservableProperty] private string _statusMessage = "选择一周或多周导出 Excel；JSON 导出全部业务数据。";

    public bool CanInteract => !IsBusy;
    public bool CanExportSelected => !IsBusy && Weeks.Any(item => item.IsSelected);
    public bool CanExportSingle => !IsBusy && Weeks.Count(item => item.IsSelected) == 1;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanExportSelected));
        OnPropertyChanged(nameof(CanExportSingle));
    }

    [RelayCommand]
    public async Task RefreshWeeksAsync() => await RunAsync(async () =>
    {
        await LoadWeeksAsync();
        StatusMessage = $"已加载 {Weeks.Count} 份周报。";
    });

    public async Task RefreshAfterImportAsync() => await RunAsync(
        LoadWeeksAsync, () => "导入已完成，但刷新周报列表失败。");

    public async Task ExportAsync(string path, BackupFileFormat format, bool overwrite)
    {
        if (!dialogs.Confirm(
            "导出文件包含明文业务数据，请妥善保管。JSON 包含全部业务数据；Excel 包含选中的周次。继续吗？",
            "导出提示"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var document = await service.CaptureAsync();
            if (format is BackupFileFormat.Excel or BackupFileFormat.Markdown)
            {
                var selected = Weeks.Where(item => item.IsSelected)
                    .Select(item => item.Report.Week.Start).ToArray();
                if (format == BackupFileFormat.Markdown && selected.Length != 1)
                {
                    throw new ArgumentException("Markdown 导出必须选择一份周报。");
                }

                document = BackupSelection.SelectWeeks(document, selected);
            }

            await files.ExportAsync(path, format, document, overwrite);
            StatusMessage = format == BackupFileFormat.Json
                ? "JSON 业务数据已导出。"
                : $"已导出 {document.Reports.Count} 份周报。";
        }, lockNavigation: true);
    }

    public async Task<bool> ImportAsync(string path, BackupFileFormat format)
    {
        var imported = false;
        await RunAsync(async () =>
        {
            var document = await files.ImportAsync(path, format);
            var plan = await service.PrepareImportAsync(document);
            var policy = reviews.ChooseImportPolicy(plan);
            if (policy is null)
            {
                StatusMessage = "已取消导入，数据库未修改。";
                return;
            }

            var result = await service.ApplyImportAsync(plan, policy.Value, userConfirmed: true);
            imported = true;
            StatusMessage = $"导入完成：新增 {result.Added}，覆盖 {result.Overwritten}，跳过 {result.Skipped}。";
            if (document.Entries.Any(item => item.Kind == BackupEntryKind.Git))
            {
                StatusMessage += " 请按需重算受影响日期的 Git 估算工时。";
            }
        }, lockNavigation: true);
        return imported;
    }

    private async Task LoadWeeksAsync()
    {
        var selected = Weeks.Where(item => item.IsSelected).Select(item => item.Report.Week.Start).ToHashSet();
        var history = await reports.ListAsync();
        Weeks.Clear();
        foreach (var report in history)
        {
            var item = new BackupWeekItem(report) { IsSelected = selected.Contains(report.Week.Start) };
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BackupWeekItem.IsSelected))
                {
                    OnPropertyChanged(nameof(CanExportSelected));
                    OnPropertyChanged(nameof(CanExportSingle));
                }
            };
            Weeks.Add(item);
        }

        OnPropertyChanged(nameof(CanExportSelected));
        OnPropertyChanged(nameof(CanExportSingle));
    }

    private async Task RunAsync(
        Func<Task> action, Func<string>? partialSuccess = null, bool lockNavigation = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsNavigationLocked = lockNavigation;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            logger.LogWarning("Backup operation failed: {ErrorType}", exception.GetType().Name);
            var prefix = partialSuccess?.Invoke();
            StatusMessage = prefix is null
                ? ErrorMessages.Describe(exception)
                : $"{prefix} {ErrorMessages.Describe(exception)}";
        }
        finally
        {
            IsBusy = false;
            IsNavigationLocked = false;
        }
    }
}
