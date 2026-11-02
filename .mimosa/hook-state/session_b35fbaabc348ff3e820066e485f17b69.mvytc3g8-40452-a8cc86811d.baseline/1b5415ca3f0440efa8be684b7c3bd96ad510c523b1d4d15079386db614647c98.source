using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WeeklyReport.App.Services;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.App.ViewModels;

public sealed class WeeklyReportHistoryItem(WeeklyReportSnapshot report)
{
    public WeeklyReportSnapshot Report { get; } = report;
    public string WeekDisplay => $"{Report.Week.Start:yyyy-MM-dd} 至 {Report.Week.EndInclusive:yyyy-MM-dd}";
    public string HoursDisplay =>
        $"已确认 {WorkHourParser.Format(Report.ConfirmedMinutes, CultureInfo.CurrentCulture)} 小时，" +
        $"估算 {WorkHourParser.Format(Report.EstimatedMinutes, CultureInfo.CurrentCulture)} 小时，" +
        $"未填写 {Report.UnknownHoursCount} 项";
}

public partial class WeeklyReportsViewModel(
    WeeklyReportService service,
    AiDraftCoordinator aiDrafts,
    IUserDialogs dialogs,
    ILogger<WeeklyReportsViewModel> logger) : ObservableObject
{
    private WeeklyReportSnapshot? _existing;
    private WeeklyReportDraft? _draft;
    private bool _changingEditor;
    private string _baselineWork = string.Empty;
    private string _baselinePlan = string.Empty;
    private string _baselineRisks = string.Empty;
    private CancellationTokenSource? _operationCancellation;

    public ObservableCollection<WeeklyReportHistoryItem> History { get; } = [];

    [ObservableProperty] private DateTime? _selectedDate = DateTime.Today;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _editorTitle = "打开或生成周报";
    [ObservableProperty] private string _workSummary = string.Empty;
    [ObservableProperty] private string _nextWeekPlan = string.Empty;
    [ObservableProperty] private string _risks = string.Empty;
    [ObservableProperty] private string _hoursDisplay = "尚未打开周报。";
    [ObservableProperty] private string _statusMessage = "选择日期后打开或生成周报。";
    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isCancellable;

    public bool CanInteract => !IsBusy;
    public bool CanEdit => HasReport && !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanEdit));
    }

    partial void OnHasReportChanged(bool value) => OnPropertyChanged(nameof(CanEdit));
    partial void OnWorkSummaryChanged(string value) => UpdateDirty();
    partial void OnNextWeekPlanChanged(string value) => UpdateDirty();
    partial void OnRisksChanged(string value) => UpdateDirty();

    [RelayCommand]
    public async Task RefreshHistoryAsync() => await RunAsync(async () =>
    {
        await LoadHistoryAsync();
        StatusMessage = $"已加载 {History.Count} 份历史周报。";
    });

    [RelayCommand]
    private async Task OpenOrCreateAsync()
    {
        if (!CanDiscardChanges())
        {
            return;
        }

        await OpenOrCreateCoreAsync();
    }

    public async Task OpenWeekAsync(DateOnly weekStart)
    {
        await WaitUntilIdleAsync();
        if (!CanDiscardChanges())
        {
            return;
        }

        SelectedDate = weekStart.ToDateTime(TimeOnly.MinValue);
        await OpenOrCreateCoreAsync();
    }

    private async Task OpenOrCreateCoreAsync() =>
        await RunAsync(async () =>
        {
            if (SelectedDate is not DateTime selected)
            {
                throw new ArgumentException("请选择周报日期。");
            }

            var result = await service.OpenOrCreateDraftAsync(DateOnly.FromDateTime(selected));
            if (result.Existing is not null)
            {
                LoadExisting(result.Existing);
                StatusMessage = "已打开已有周报，未重新生成或覆盖。";
            }
            else if (result.Draft is not null)
            {
                LoadDraft(result.Draft);
                StatusMessage = "草稿已生成，请编辑后保存。";
            }
            else
            {
                throw new InvalidOperationException("周报结果无效。");
            }
        });

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!HasReport)
        {
            StatusMessage = "请先打开或生成周报。";
            return;
        }

        var saved = false;
        await RunAsync(async () =>
        {
            WeeklyReportSnapshot report;
            if (_existing is not null)
            {
                report = await service.UpdateAsync(_existing, WorkSummary, NextWeekPlan, Risks);
            }
            else if (_draft is not null)
            {
                report = await service.SaveNewAsync(_draft, WorkSummary, NextWeekPlan, Risks);
            }
            else
            {
                throw new InvalidOperationException("周报编辑状态无效。");
            }

            saved = true;
            LoadExisting(report);
            await LoadHistoryAsync();
            StatusMessage = "周报已保存。";
        }, partialSuccess: () => saved ? "周报已保存，但刷新历史失败。" : null);
    }

    [RelayCommand]
    private void OpenHistory(WeeklyReportHistoryItem? item)
    {
        if (item is null || !CanDiscardChanges())
        {
            return;
        }

        LoadExisting(item.Report);
        SelectedTabIndex = 0;
        StatusMessage = "已打开历史周报。";
    }

    [RelayCommand]
    private async Task DeleteHistoryAsync(WeeklyReportHistoryItem? item)
    {
        if (item is null || !dialogs.Confirm("确定删除此周报？此操作不可撤销。", "删除周报"))
        {
            return;
        }

        if (_existing?.Id == item.Report.Id && !CanDiscardChanges())
        {
            return;
        }

        var deleted = false;
        await RunAsync(async () =>
        {
            await service.DeleteAsync(item.Report);
            deleted = true;
            if (_existing?.Id == item.Report.Id)
            {
                ResetEditor();
            }

            await LoadHistoryAsync();
            StatusMessage = "周报已删除。";
        }, partialSuccess: () => deleted ? "周报已删除，但刷新历史失败。" : null);
    }

    [RelayCommand]
    private async Task GenerateWithAiAsync()
    {
        if (!HasReport || string.IsNullOrWhiteSpace(WorkSummary))
        {
            StatusMessage = "请先打开周报并填写本周工作。";
            return;
        }

        await RunAsync(async () =>
        {
            _operationCancellation = new CancellationTokenSource();
            IsCancellable = true;
            var result = await aiDrafts.GenerateAsync(
                "根据用户提供的工作记录生成简洁、清晰的本周工作总结，不要虚构事实。",
                WorkSummary,
                _operationCancellation.Token);
            if (result is null)
            {
                StatusMessage = "已取消 DeepSeek 发送或应用。";
                return;
            }

            WorkSummary = result;
            StatusMessage = "DeepSeek 结果已应用到编辑区，请核对后保存。";
        }, canCancel: true);
    }

    [RelayCommand]
    private void CancelOperation() => _operationCancellation?.Cancel();

    [RelayCommand]
    private void CloseReport()
    {
        if (CanDiscardChanges())
        {
            ResetEditor();
            StatusMessage = "已关闭周报编辑。";
        }
    }

    public bool CanDiscardChanges() =>
        !IsDirty || dialogs.Confirm("周报有未保存的修改，确定放弃吗？", "未保存的修改");

    public async Task RefreshAfterImportAsync()
    {
        ResetEditor();
        await RefreshHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        var reports = await service.GetHistoryAsync();
        History.Clear();
        foreach (var report in reports)
        {
            History.Add(new WeeklyReportHistoryItem(report));
        }
    }

    private void LoadExisting(WeeklyReportSnapshot report)
    {
        _changingEditor = true;
        _existing = report;
        _draft = null;
        SelectedDate = report.Week.Start.ToDateTime(TimeOnly.MinValue);
        WorkSummary = report.WorkSummary;
        NextWeekPlan = report.NextWeekPlan;
        Risks = report.Risks;
        EditorTitle = $"{report.Week.Start:yyyy-MM-dd} 至 {report.Week.EndInclusive:yyyy-MM-dd}";
        HoursDisplay = DescribeHours(report.ConfirmedMinutes, report.EstimatedMinutes, report.UnknownHoursCount);
        SetBaseline();
        _changingEditor = false;
        HasReport = true;
        IsDirty = false;
    }

    private void LoadDraft(WeeklyReportDraft draft)
    {
        _changingEditor = true;
        _existing = null;
        _draft = draft;
        SelectedDate = draft.Week.Start.ToDateTime(TimeOnly.MinValue);
        WorkSummary = draft.WorkSummary;
        NextWeekPlan = string.Empty;
        Risks = string.Empty;
        EditorTitle = $"{draft.Week.Start:yyyy-MM-dd} 至 {draft.Week.EndInclusive:yyyy-MM-dd}（草稿）";
        HoursDisplay = DescribeHours(draft.ConfirmedMinutes, draft.EstimatedMinutes, draft.UnknownHoursCount);
        SetBaseline();
        _changingEditor = false;
        HasReport = true;
        IsDirty = false;
    }

    private void ResetEditor()
    {
        _changingEditor = true;
        _existing = null;
        _draft = null;
        WorkSummary = string.Empty;
        NextWeekPlan = string.Empty;
        Risks = string.Empty;
        EditorTitle = "打开或生成周报";
        HoursDisplay = "尚未打开周报。";
        SetBaseline();
        _changingEditor = false;
        HasReport = false;
        IsDirty = false;
    }

    private void SetBaseline()
    {
        _baselineWork = WorkSummary;
        _baselinePlan = NextWeekPlan;
        _baselineRisks = Risks;
    }

    private void UpdateDirty()
    {
        if (!_changingEditor)
        {
            IsDirty = WorkSummary != _baselineWork || NextWeekPlan != _baselinePlan || Risks != _baselineRisks;
        }
    }

    private static string DescribeHours(int confirmed, int estimated, int unknown) =>
        $"已确认 {WorkHourParser.Format(confirmed, CultureInfo.CurrentCulture)} 小时，" +
        $"估算 {WorkHourParser.Format(estimated, CultureInfo.CurrentCulture)} 小时，未填写 {unknown} 项。";

    private async Task RunAsync(Func<Task> action, bool canCancel = false, Func<string?>? partialSuccess = null)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (canCancel && _operationCancellation?.IsCancellationRequested == true)
        {
            StatusMessage = "操作已取消，未保存任何更改。";
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            logger.LogWarning("Weekly report operation failed: {ErrorType}", exception.GetType().Name);
            var prefix = partialSuccess?.Invoke();
            StatusMessage = prefix is null
                ? ErrorMessages.Describe(exception)
                : $"{prefix} {ErrorMessages.Describe(exception)}";
        }
        finally
        {
            IsCancellable = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private Task WaitUntilIdleAsync()
    {
        if (!IsBusy)
        {
            return Task.CompletedTask;
        }

        StatusMessage = "当前操作完成后将打开提醒对应的周报。";
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(IsBusy) && !IsBusy)
            {
                PropertyChanged -= handler;
                completion.TrySetResult();
            }
        };
        PropertyChanged += handler;
        if (!IsBusy)
        {
            PropertyChanged -= handler;
            completion.TrySetResult();
        }

        return completion.Task;
    }
}
