using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WeeklyReport.App.Services;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.App.ViewModels;

public sealed record GitAuthorOption(GitAuthorField Value, string Label);

public sealed partial class GitProjectItem(GitProject project) : ObservableObject
{
    public GitProject Project { get; } = project;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed class GitWorkItemView(GitReportWorkItem item)
{
    public GitReportWorkItem Item { get; } = item;

    public string HoursDisplay => Item.ConfirmedMinutes is int confirmed
        ? $"已确认：{WorkHourParser.Format(confirmed, CultureInfo.CurrentCulture)} 小时"
        : Item.EstimatedMinutes is int estimated
            ? $"估算：{WorkHourParser.Format(estimated, CultureInfo.CurrentCulture)} 小时"
            : "工时无法推断";
}

public partial class GitProjectsViewModel : ObservableObject
{
    private readonly IGitProjectRepository _projects;
    private readonly IGitSyncStore _store;
    private readonly GitSyncService _sync;
    private readonly WorkEntryService _manualEntries;
    private readonly AiDraftCoordinator _aiDrafts;
    private readonly IUserDialogs _dialogs;
    private readonly ILogger<GitProjectsViewModel> _logger;
    private Guid? _editingProjectId;
    private Guid? _editingWorkItemId;
    private bool _changingProjectEditor;
    private bool _changingWorkEditor;
    private string _baselineProjectName = string.Empty;
    private string _baselineRepositoryPath = string.Empty;
    private string _baselineAuthor = string.Empty;
    private GitAuthorField _baselineAuthorField = GitAuthorField.Email;
    private string _baselineDescription = string.Empty;
    private string _baselineHours = string.Empty;
    private PreparedGitSync? _prepared;
    private CancellationTokenSource? _syncCancellation;

    public GitProjectsViewModel(
        IGitProjectRepository projects,
        IGitSyncStore store,
        GitSyncService sync,
        WorkEntryService manualEntries,
        AiDraftCoordinator aiDrafts,
        IUserDialogs dialogs,
        ILogger<GitProjectsViewModel> logger)
    {
        _projects = projects;
        _store = store;
        _sync = sync;
        _manualEntries = manualEntries;
        _aiDrafts = aiDrafts;
        _dialogs = dialogs;
        _logger = logger;
        var week = WeekPeriod.FromDate(DateOnly.FromDateTime(DateTime.Today));
        _startDate = week.Start.ToDateTime(TimeOnly.MinValue);
        _endDate = week.EndInclusive.ToDateTime(TimeOnly.MinValue);
    }

    public ObservableCollection<GitProjectItem> Projects { get; } = [];
    public ObservableCollection<GitWorkItemView> WorkItems { get; } = [];
    public IReadOnlyList<GitAuthorOption> AuthorOptions { get; } =
    [
        new(GitAuthorField.Email, "邮箱"),
        new(GitAuthorField.Name, "姓名")
    ];

    [ObservableProperty] private string _projectName = string.Empty;
    [ObservableProperty] private string _repositoryPath = string.Empty;
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private GitAuthorField _authorField = GitAuthorField.Email;
    [ObservableProperty] private string _projectEditorTitle = "新增项目";
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private DateTime? _workWeek = DateTime.Today;
    [ObservableProperty] private string _workDescription = string.Empty;
    [ObservableProperty] private string _workHours = string.Empty;
    [ObservableProperty] private string _workEditorTitle = "选择工作项进行编辑";
    [ObservableProperty] private string _previewText = "选择项目和日期后，可预览 Git 同步结果。";
    [ObservableProperty] private string _statusMessage = "请先配置本地项目。";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private bool _isProjectDirty;
    [ObservableProperty] private bool _isWorkDirty;
    [ObservableProperty] private bool _hasWorkSelection;

    public bool CanInteract => !IsBusy;
    public bool CanApplyPreview => HasPreview && !IsBusy;
    public bool CanEditWorkItem => HasWorkSelection && !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanApplyPreview));
        OnPropertyChanged(nameof(CanEditWorkItem));
    }

    partial void OnHasPreviewChanged(bool value) => OnPropertyChanged(nameof(CanApplyPreview));
    partial void OnHasWorkSelectionChanged(bool value) => OnPropertyChanged(nameof(CanEditWorkItem));
    partial void OnProjectNameChanged(string value) => UpdateProjectDirty();
    partial void OnRepositoryPathChanged(string value) => UpdateProjectDirty();
    partial void OnAuthorChanged(string value) => UpdateProjectDirty();
    partial void OnAuthorFieldChanged(GitAuthorField value) => UpdateProjectDirty();
    partial void OnWorkDescriptionChanged(string value) => UpdateWorkDirty();
    partial void OnWorkHoursChanged(string value) => UpdateWorkDirty();
    partial void OnStartDateChanged(DateTime? value) => InvalidatePreview();
    partial void OnEndDateChanged(DateTime? value) => InvalidatePreview();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!CanDiscardWorkChanges())
        {
            return;
        }

        await RunAsync(async () =>
        {
            await LoadProjectsAsync();
            await LoadWorkItemsAsync();
            ResetWorkEditor();
            StatusMessage = $"已加载 {Projects.Count} 个项目和 {WorkItems.Count} 条 Git 工作项。";
        });
    }

    [RelayCommand]
    private void EditProject(GitProjectItem? item)
    {
        if (item is null || !CanDiscardProjectChanges())
        {
            return;
        }

        _changingProjectEditor = true;
        _editingProjectId = item.Project.Id;
        ProjectName = item.Project.Name;
        RepositoryPath = item.Project.RepositoryPath ?? string.Empty;
        AuthorField = item.Project.AuthorField ?? GitAuthorField.Email;
        Author = item.Project.Author ?? string.Empty;
        _baselineProjectName = ProjectName;
        _baselineRepositoryPath = RepositoryPath;
        _baselineAuthorField = AuthorField;
        _baselineAuthor = Author;
        ProjectEditorTitle = "编辑项目";
        _changingProjectEditor = false;
        IsProjectDirty = false;
    }

    [RelayCommand]
    private void NewProject()
    {
        if (CanDiscardProjectChanges())
        {
            ResetProjectEditor();
        }
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        var saved = false;
        await RunAsync(async () =>
        {
            if (!Directory.Exists(RepositoryPath))
            {
                throw new ArgumentException("仓库目录不存在或不可访问。");
            }

            var project = new GitProject(
                _editingProjectId ?? Guid.NewGuid(), ProjectName, RepositoryPath, AuthorField, Author);
            if (_editingProjectId is not null)
            {
                if (!await _projects.UpdateAsync(project))
                {
                    throw new KeyNotFoundException("项目不存在，可能已被删除。");
                }
            }
            else
            {
                await _projects.AddAsync(project);
            }

            saved = true;
            ResetProjectEditor();
            InvalidatePreview();
            await LoadProjectsAsync();
            StatusMessage = "项目配置已保存。";
        }, partialSuccess: () => saved ? "项目已保存，但刷新列表失败。" : null);
    }

    [RelayCommand]
    private async Task DeleteProjectAsync(GitProjectItem? item)
    {
        if (item is null || !_dialogs.Confirm("确定删除此项目？有关联工作项的项目不能删除。", "删除项目"))
        {
            return;
        }

        if (_editingProjectId == item.Project.Id && !CanDiscardProjectChanges())
        {
            return;
        }

        var deleted = false;
        await RunAsync(async () =>
        {
            if (!await _projects.DeleteAsync(item.Project.Id))
            {
                throw new KeyNotFoundException("项目不存在，可能已被删除。");
            }

            deleted = true;
            if (_editingProjectId == item.Project.Id)
            {
                ResetProjectEditor();
            }

            InvalidatePreview();
            await LoadProjectsAsync();
            StatusMessage = "项目已删除。";
        }, partialSuccess: () => deleted ? "项目已删除，但刷新列表失败。" : null);
    }

    [RelayCommand]
    private async Task PrepareSyncAsync()
    {
        if (IsProjectDirty && !_dialogs.Confirm("项目配置有未保存的修改，将使用已保存配置。继续吗？", "Git 同步"))
        {
            return;
        }

        InvalidatePreview();
        await RunAsync(async () =>
        {
            if (StartDate is not DateTime start || EndDate is not DateTime end)
            {
                throw new ArgumentException("请选择同步开始和结束日期。");
            }

            var selected = Projects.Where(item => item.IsSelected).Select(item => item.Project).ToArray();
            _syncCancellation = new CancellationTokenSource();
            IsSyncing = true;
            var prepared = await _sync.PrepareAsync(
                selected, DateOnly.FromDateTime(start), DateOnly.FromDateTime(end),
                TimeZoneInfo.Local, _syncCancellation.Token);
            var previewText = await DescribePreviewAsync(prepared);
            _syncCancellation.Token.ThrowIfCancellationRequested();
            _prepared = prepared;
            PreviewText = previewText;
            HasPreview = true;
            StatusMessage = "同步预览已生成；确认前不会修改数据库。";
        }, canCancel: true);
    }

    [RelayCommand]
    private void CancelSync() => _syncCancellation?.Cancel();

    [RelayCommand]
    private async Task ApplySyncAsync()
    {
        var prepared = _prepared;
        if (prepared is null)
        {
            StatusMessage = "请先生成 Git 同步预览。";
            return;
        }

        if (!CanDiscardWorkChanges())
        {
            return;
        }

        var skipDeleted = prepared.Preview.DeletedConflicts.Count == 0
            || _dialogs.Confirm("有已删除工作项收到新提交。跳过这些工作项并继续吗？", "Git 同步");
        if (!skipDeleted)
        {
            return;
        }

        var acceptShallow = !prepared.Statuses.Any(status => status.IsShallow)
            || _dialogs.Confirm("所选仓库包含浅克隆，历史可能不完整。仍要继续吗？", "Git 同步");
        if (!acceptShallow
            || !_dialogs.Confirm("确认应用预览中的 Git 提交和工时建议？已有描述和已确认工时不会被覆盖。", "Git 同步"))
        {
            return;
        }

        var applied = false;
        await RunAsync(async () =>
        {
            var result = await _sync.ApplyAsync(prepared, true, skipDeleted, acceptShallow);
            applied = true;
            InvalidatePreview();
            ResetWorkEditor();
            await LoadWorkItemsAsync();
            StatusMessage = $"同步完成：新增 {result.Created}，更新 {result.Updated}，" +
                $"重复 {result.DuplicateCount}，已删除冲突 {result.DeletedConflictCount}。";
        }, partialSuccess: () => applied ? "同步已保存，但刷新列表失败。" : null);
    }

    [RelayCommand]
    private void EditWorkItem(GitWorkItemView? view)
    {
        if (view is null || !CanDiscardWorkChanges())
        {
            return;
        }

        _changingWorkEditor = true;
        _editingWorkItemId = view.Item.Id;
        WorkDescription = view.Item.Description;
        WorkHours = WorkHourParser.Format(view.Item.ConfirmedMinutes, CultureInfo.CurrentCulture);
        _baselineDescription = WorkDescription;
        _baselineHours = WorkHours;
        WorkEditorTitle = $"{view.Item.ProjectName} · {view.Item.WorkDate:yyyy-MM-dd}";
        _changingWorkEditor = false;
        IsWorkDirty = false;
        HasWorkSelection = true;
    }

    [RelayCommand]
    private async Task SaveWorkItemAsync()
    {
        if (_editingWorkItemId is not Guid id)
        {
            StatusMessage = "请先选择 Git 工作项。";
            return;
        }

        var saved = false;
        await RunAsync(async () =>
        {
            var item = WorkItems.Select(view => view.Item).SingleOrDefault(value => value.Id == id)
                ?? throw new KeyNotFoundException("Git 工作项不存在，请重新加载。");
            var minutes = WorkHourParser.Parse(WorkHours, CultureInfo.CurrentCulture);
            if (minutes is not null && !await ConfirmDailyHoursAsync(item, minutes.Value))
            {
                return;
            }

            if (!await _store.UpdateWorkItemAsync(id, WorkDescription, minutes))
            {
                throw new KeyNotFoundException("Git 工作项不存在或已发生变化，请重新加载。");
            }

            saved = true;
            ResetWorkEditor();
            InvalidatePreview();
            await LoadWorkItemsAsync();
            StatusMessage = "Git 工作项已保存。";
        }, partialSuccess: () => saved ? "工作项已保存，但刷新列表失败。" : null);
    }

    [RelayCommand]
    private async Task PolishWorkItemAsync()
    {
        if (_editingWorkItemId is null || string.IsNullOrWhiteSpace(WorkDescription))
        {
            StatusMessage = "请先选择并填写 Git 工作项描述。";
            return;
        }

        await RunAsync(async () =>
        {
            _syncCancellation = new CancellationTokenSource();
            IsSyncing = true;
            var result = await _aiDrafts.GenerateAsync(
                "根据用户提供的 Git 工作项描述进行简洁润色，不要虚构事实。",
                WorkDescription,
                _syncCancellation.Token);
            if (result is null)
            {
                StatusMessage = "已取消 DeepSeek 发送或应用。";
                return;
            }

            WorkDescription = result;
            StatusMessage = "DeepSeek 结果已应用到编辑区，请核对后保存。";
        }, canCancel: true);
    }

    [RelayCommand]
    private async Task DeleteWorkItemAsync(GitWorkItemView? view)
    {
        if (view is null || !_dialogs.Confirm(
            "确定删除此 Git 工作项？其提交将保留去重记录，不会在下次同步时静默恢复。", "删除工作项"))
        {
            return;
        }

        if (_editingWorkItemId == view.Item.Id && !CanDiscardWorkChanges())
        {
            return;
        }

        var deleted = false;
        await RunAsync(async () =>
        {
            if (!await _store.SoftDeleteWorkItemAsync(view.Item.Id))
            {
                throw new KeyNotFoundException("Git 工作项不存在或已发生变化，请重新加载。");
            }

            deleted = true;
            if (_editingWorkItemId == view.Item.Id)
            {
                ResetWorkEditor();
            }

            InvalidatePreview();
            await LoadWorkItemsAsync();
            StatusMessage = "Git 工作项已删除，相关估算已重新计算。";
        }, partialSuccess: () => deleted ? "工作项已删除，但刷新列表失败。" : null);
    }

    [RelayCommand]
    private void CancelWorkEdit()
    {
        if (CanDiscardWorkChanges())
        {
            ResetWorkEditor();
        }
    }

    [RelayCommand]
    private async Task RecalculateDateAsync()
    {
        if (WorkWeek is not DateTime selected)
        {
            StatusMessage = "请选择需要重算工时的日期。";
            return;
        }

        if (!CanDiscardWorkChanges()
            || !_dialogs.Confirm("将重算该日期所有项目的自动估算，不覆盖已确认工时。继续吗？", "重算工时"))
        {
            return;
        }

        var recalculated = false;
        await RunAsync(async () =>
        {
            var changed = await _store.RecalculateDateAsync(DateOnly.FromDateTime(selected));
            recalculated = true;
            ResetWorkEditor();
            InvalidatePreview();
            await LoadWorkItemsAsync();
            StatusMessage = $"工时重算完成，更新了 {changed} 条自动估算。";
        }, partialSuccess: () => recalculated ? "工时已重算，但刷新列表失败。" : null);
    }

    public bool CanDiscardChanges() => CanDiscardProjectChanges() && CanDiscardWorkChanges();

    public void InvalidateForExternalChange() => InvalidatePreview();

    public async Task RefreshAfterImportAsync()
    {
        ResetProjectEditor();
        ResetWorkEditor();
        await RefreshAsync();
    }

    private bool CanDiscardProjectChanges() =>
        !IsProjectDirty || _dialogs.Confirm("项目配置有未保存的修改，确定放弃吗？", "未保存的修改");

    private bool CanDiscardWorkChanges() =>
        !IsWorkDirty || _dialogs.Confirm("Git 工作项有未保存的修改，确定放弃吗？", "未保存的修改");

    private async Task<bool> ConfirmDailyHoursAsync(GitReportWorkItem item, int minutes)
    {
        var week = WeekPeriod.FromDate(item.WorkDate);
        var manual = await _manualEntries.GetByWeekAsync(item.WorkDate);
        var git = await _store.GetByWeekAsync(week);
        var total = manual.Where(entry => entry.WorkDate == item.WorkDate)
                .Sum(entry => (long)(entry.ConfirmedMinutes ?? 0))
            + git.Where(other => other.WorkDate == item.WorkDate && other.Id != item.Id)
                .Sum(other => (long)(other.ConfirmedMinutes ?? other.EstimatedMinutes ?? 0))
            + minutes;
        return total <= 8 * 60
            || _dialogs.Confirm("当天已填写或估算的工时合计超过 8 小时，仍要保存吗？", "工时提示");
    }

    private async Task LoadProjectsAsync()
    {
        var selected = Projects.Where(item => item.IsSelected).Select(item => item.Project.Id).ToHashSet();
        var projects = await _projects.ListAsync();
        Projects.Clear();
        foreach (var project in projects)
        {
            var item = new GitProjectItem(project) { IsSelected = selected.Contains(project.Id) };
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GitProjectItem.IsSelected))
                {
                    InvalidatePreview();
                }
            };
            Projects.Add(item);
        }

        InvalidatePreview();
    }

    private async Task LoadWorkItemsAsync()
    {
        if (WorkWeek is not DateTime selected)
        {
            throw new ArgumentException("请选择工作项查询日期。");
        }

        var items = await _store.GetByWeekAsync(WeekPeriod.FromDate(DateOnly.FromDateTime(selected)));
        WorkItems.Clear();
        foreach (var item in items)
        {
            WorkItems.Add(new GitWorkItemView(item));
        }
    }

    private async Task<string> DescribePreviewAsync(PreparedGitSync prepared)
    {
        var names = Projects.ToDictionary(item => item.Project.Id, item => item.Project.Name);
        var manualByWeek = new Dictionary<DateOnly, IReadOnlyList<WorkEntry>>();
        string ProjectNameFor(Guid id) => names.GetValueOrDefault(id) ?? "未知项目";
        var text = new StringBuilder();
        text.AppendLine($"新增或更新工作项：{prepared.Preview.Proposals.Count}；重复提交：{prepared.Preview.DuplicateCount}。");
        foreach (var status in prepared.Statuses)
        {
            if (!status.HasHead)
            {
                text.AppendLine($"{ProjectNameFor(status.ProjectId)}：仓库暂无提交。");
            }
            if (status.IsShallow)
            {
                text.AppendLine($"{ProjectNameFor(status.ProjectId)}：浅克隆，历史可能不完整。");
            }
        }

        foreach (var proposal in prepared.Preview.Proposals)
        {
            text.AppendLine();
            text.AppendLine($"{ProjectNameFor(proposal.ProjectId)} · {proposal.WorkDate:yyyy-MM-dd} · 新提交 {proposal.AddedCommits.Count}");
            if (proposal.CurrentDescription is not null)
            {
                text.AppendLine("当前描述（将保留）：");
                text.AppendLine(proposal.CurrentDescription);
            }
            text.AppendLine("建议描述：");
            text.AppendLine(proposal.SuggestedDescription);
            if (proposal.ConfirmedMinutes is int confirmed)
            {
                text.AppendLine($"已确认工时（将保留）：{WorkHourParser.Format(confirmed, CultureInfo.CurrentCulture)} 小时");
            }
        }

        foreach (var day in prepared.Preview.SuggestedHours)
        {
            text.AppendLine();
            text.AppendLine($"{day.Date:yyyy-MM-dd} 自动估算：");
            foreach (var project in day.Projects)
            {
                var hours = project.Minutes is int minutes
                    ? $"{WorkHourParser.Format(minutes, CultureInfo.CurrentCulture)} 小时"
                    : "无法推断";
                text.AppendLine($"- {ProjectNameFor(project.ProjectId)}：{hours}");
            }

            var week = WeekPeriod.FromDate(day.Date);
            if (!manualByWeek.TryGetValue(week.Start, out var manual))
            {
                manual = await _manualEntries.GetByWeekAsync(day.Date);
                manualByWeek.Add(week.Start, manual);
            }

            var confirmedByProject = prepared.Baseline
                .Where(item => !item.IsDeleted && item.WorkDate == day.Date)
                .ToDictionary(item => item.ProjectId, item => item.ConfirmedMinutes);
            var knownTotal = manual.Where(entry => entry.WorkDate == day.Date)
                    .Sum(entry => (long)(entry.ConfirmedMinutes ?? 0))
                + day.Projects.Sum(project =>
                    (long)(confirmedByProject.GetValueOrDefault(project.ProjectId) ?? project.Minutes ?? 0));
            if (knownTotal > 8 * 60)
            {
                text.AppendLine("提示：当天已确认和预计工时合计超过 8 小时，请核对后确认。");
            }
        }

        foreach (var conflict in prepared.Preview.DeletedConflicts)
        {
            text.AppendLine();
            text.AppendLine($"{ProjectNameFor(conflict.ProjectId)} · {conflict.WorkDate:yyyy-MM-dd} 已删除，" +
                $"有 {conflict.NewCommits.Count} 条新提交需要明确跳过。");
        }

        return text.ToString().TrimEnd();
    }

    private void ResetProjectEditor()
    {
        _changingProjectEditor = true;
        _editingProjectId = null;
        ProjectName = string.Empty;
        RepositoryPath = string.Empty;
        Author = string.Empty;
        AuthorField = GitAuthorField.Email;
        _baselineProjectName = ProjectName;
        _baselineRepositoryPath = RepositoryPath;
        _baselineAuthor = Author;
        _baselineAuthorField = AuthorField;
        ProjectEditorTitle = "新增项目";
        _changingProjectEditor = false;
        IsProjectDirty = false;
    }

    private void ResetWorkEditor()
    {
        _changingWorkEditor = true;
        _editingWorkItemId = null;
        WorkDescription = string.Empty;
        WorkHours = string.Empty;
        _baselineDescription = WorkDescription;
        _baselineHours = WorkHours;
        WorkEditorTitle = "选择工作项进行编辑";
        _changingWorkEditor = false;
        IsWorkDirty = false;
        HasWorkSelection = false;
    }

    private void UpdateProjectDirty()
    {
        if (!_changingProjectEditor)
        {
            IsProjectDirty = ProjectName != _baselineProjectName || RepositoryPath != _baselineRepositoryPath
                || Author != _baselineAuthor || AuthorField != _baselineAuthorField;
        }
    }

    private void UpdateWorkDirty()
    {
        if (!_changingWorkEditor)
        {
            IsWorkDirty = WorkDescription != _baselineDescription || WorkHours != _baselineHours;
        }
    }

    private void InvalidatePreview()
    {
        if (_prepared is not null)
        {
            _prepared = null;
            HasPreview = false;
            PreviewText = "数据或选择已变化，请重新预览。";
        }
    }

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
        catch (OperationCanceledException) when (canCancel && _syncCancellation?.IsCancellationRequested == true)
        {
            StatusMessage = "操作已取消，未保存任何更改。";
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            _logger.LogWarning("Git operation failed: {ErrorType}", exception.GetType().Name);
            var prefix = partialSuccess?.Invoke();
            StatusMessage = prefix is null
                ? ErrorMessages.Describe(exception)
                : $"{prefix} {ErrorMessages.Describe(exception)}";
        }
        finally
        {
            IsSyncing = false;
            _syncCancellation?.Dispose();
            _syncCancellation = null;
            IsBusy = false;
        }
    }
}
