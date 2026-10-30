using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WeeklyReport.App.Services;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.App.ViewModels;

public partial class WorkEntriesViewModel(
    WorkEntryService service,
    IGitReportWorkItemSource gitItems,
    IUserDialogs dialogs,
    ILogger<WorkEntriesViewModel> logger) : ObservableObject
{
    public event EventHandler? DataChanged;

    private Guid? _editingId;
    private bool _changingEditor;
    private string _baselineContent = string.Empty;
    private DateTime? _baselineDate = DateTime.Today;
    private string _baselineHours = string.Empty;

    public ObservableCollection<WorkEntry> Entries { get; } = [];

    [ObservableProperty]
    private DateTime? _selectedWeek = DateTime.Today;

    [ObservableProperty]
    private DateTime? _editDate = DateTime.Today;

    [ObservableProperty]
    private string _editContent = string.Empty;

    [ObservableProperty]
    private string _editHours = string.Empty;

    [ObservableProperty]
    private string _editorTitle = "新建工作记录";

    [ObservableProperty]
    private string _statusMessage = "请选择日期并加载工作记录。";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDirty;

    public bool CanInteract => !IsBusy;

    public string WeekLabel
    {
        get
        {
            if (SelectedWeek is not DateTime selected)
            {
                return "请选择日期";
            }

            try
            {
                var week = WeekPeriod.FromDate(DateOnly.FromDateTime(selected));
                return $"{week.Start:yyyy-MM-dd} 至 {week.EndInclusive:yyyy-MM-dd}";
            }
            catch (ArgumentOutOfRangeException)
            {
                return "日期超出可计算范围";
            }
        }
    }

    partial void OnSelectedWeekChanged(DateTime? value) => OnPropertyChanged(nameof(WeekLabel));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    partial void OnEditDateChanged(DateTime? value) => UpdateDirty();
    partial void OnEditContentChanged(string value) => UpdateDirty();
    partial void OnEditHoursChanged(string value) => UpdateDirty();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!CanDiscardChanges())
        {
            return;
        }

        await RunAsync(async () =>
        {
            await ReloadAsync();
            ResetEditor();
            StatusMessage = $"已加载 {Entries.Count} 条工作记录。";
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var saved = false;
        await RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(EditContent))
            {
                throw new ArgumentException("请输入工作记录内容。");
            }

            if (EditDate is not DateTime selectedDate)
            {
                throw new ArgumentException("请选择工作日期。");
            }

            var date = DateOnly.FromDateTime(selectedDate);
            var minutes = WorkHourParser.Parse(EditHours, System.Globalization.CultureInfo.CurrentCulture);
            if (minutes is not null)
            {
                var existing = await service.GetByWeekAsync(date);
                var git = await gitItems.GetByWeekAsync(WeekPeriod.FromDate(date));
                var dailyMinutes = existing
                    .Where(item => item.WorkDate == date && item.Id != _editingId)
                    .Sum(item => (long)(item.ConfirmedMinutes ?? 0))
                    + git.Where(item => item.WorkDate == date)
                        .Sum(item => (long)(item.ConfirmedMinutes ?? item.EstimatedMinutes ?? 0))
                    + minutes.Value;
                if (dailyMinutes > 8 * 60
                    && !dialogs.Confirm("当天已填写或估算的工时合计超过 8 小时，仍要保存吗？", "工时提示"))
                {
                    return;
                }
            }

            if (_editingId is Guid id)
            {
                await service.UpdateAsync(id, date, EditContent, minutes);
            }
            else
            {
                await service.CreateAsync(date, EditContent, minutes);
            }

            saved = true;
            DataChanged?.Invoke(this, EventArgs.Empty);
            SelectedWeek = selectedDate;
            ResetEditor();
            await ReloadAsync();
            StatusMessage = "工作记录已保存。";
        }, () => saved ? "记录已保存，但刷新列表失败。" : null);
    }

    [RelayCommand]
    private void Edit(WorkEntry? entry)
    {
        if (entry is null || !CanDiscardChanges())
        {
            return;
        }

        _changingEditor = true;
        _editingId = entry.Id;
        EditDate = entry.WorkDate.ToDateTime(TimeOnly.MinValue);
        EditContent = entry.Content;
        EditHours = WorkHourParser.Format(entry.ConfirmedMinutes, System.Globalization.CultureInfo.CurrentCulture);
        _baselineDate = EditDate?.Date;
        _baselineContent = EditContent;
        _baselineHours = EditHours;
        EditorTitle = "编辑工作记录";
        _changingEditor = false;
        IsDirty = false;
    }

    [RelayCommand]
    private void New()
    {
        if (CanDiscardChanges())
        {
            ResetEditor();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(WorkEntry? entry)
    {
        if (entry is null || !dialogs.Confirm("确定删除这条工作记录？此操作不可撤销。", "删除工作记录"))
        {
            return;
        }

        if (_editingId == entry.Id && !CanDiscardChanges())
        {
            return;
        }

        var deleted = false;
        await RunAsync(async () =>
        {
            await service.DeleteAsync(entry.Id);
            deleted = true;
            DataChanged?.Invoke(this, EventArgs.Empty);
            if (_editingId == entry.Id)
            {
                ResetEditor();
            }

            await ReloadAsync();
            StatusMessage = "工作记录已删除。";
        }, () => deleted ? "记录已删除，但刷新列表失败。" : null);
    }

    public bool CanDiscardChanges() =>
        !IsDirty || dialogs.Confirm("当前工作记录有未保存的修改，确定放弃吗？", "未保存的修改");

    private async Task ReloadAsync()
    {
        if (SelectedWeek is not DateTime selected)
        {
            throw new ArgumentException("请选择查询日期。");
        }

        var entries = await service.GetByWeekAsync(DateOnly.FromDateTime(selected));
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }
    }

    private void ResetEditor()
    {
        _changingEditor = true;
        _editingId = null;
        EditDate = DateTime.Today;
        EditContent = string.Empty;
        EditHours = string.Empty;
        _baselineDate = EditDate?.Date;
        _baselineContent = EditContent;
        _baselineHours = EditHours;
        EditorTitle = "新建工作记录";
        _changingEditor = false;
        IsDirty = false;
    }

    private void UpdateDirty()
    {
        if (!_changingEditor)
        {
            IsDirty = EditContent != _baselineContent || EditDate?.Date != _baselineDate || EditHours != _baselineHours;
        }
    }

    private async Task RunAsync(Func<Task> action, Func<string?>? partialSuccess = null)
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
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            logger.LogWarning("Work entry operation failed: {ErrorType}", exception.GetType().Name);
            var prefix = partialSuccess?.Invoke();
            StatusMessage = prefix is null
                ? ErrorMessages.Describe(exception)
                : $"{prefix} {ErrorMessages.Describe(exception)}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
