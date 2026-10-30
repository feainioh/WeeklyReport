using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using WeeklyReport.App.Services;
using WeeklyReport.App.ViewModels;
using WeeklyReport.App.Views;
using WeeklyReport.Application;

namespace WeeklyReport.App;

public partial class MainWindow : Window
{
    private readonly WorkEntriesViewModel _entries;
    private readonly DatabaseSettingsViewModel _settings;
    private readonly GitProjectsViewModel _gitProjects;
    private readonly AiSettingsViewModel _aiSettings;
    private readonly WeeklyReportsViewModel _weeklyReports;
    private readonly BackupViewModel _backup;
    private readonly WeeklyReportsView _weeklyView;
    private readonly AutoStartService _autoStart;
    private readonly WindowsReminderNotifier _reminderNotifier;
    private readonly ReminderEngine _reminderEngine;
    private readonly IUserDialogs _dialogs;
    private readonly ILogger<MainWindow> _logger;
    private bool _allowClose;
    private bool _autoStartEnabled;

    public MainWindow(
        WorkEntriesViewModel entries,
        DatabaseSettingsViewModel settings,
        GitProjectsViewModel gitProjects,
        AiSettingsViewModel aiSettings,
        WeeklyReportsViewModel weeklyReports,
        BackupViewModel backup,
        AutoStartService autoStart,
        WindowsReminderNotifier reminderNotifier,
        ReminderEngine reminderEngine,
        IUserDialogs dialogs,
        ILogger<MainWindow> logger)
    {
        InitializeComponent();
        _entries = entries;
        _settings = settings;
        _gitProjects = gitProjects;
        _aiSettings = aiSettings;
        _weeklyReports = weeklyReports;
        _backup = backup;
        _autoStart = autoStart;
        _reminderNotifier = reminderNotifier;
        _reminderEngine = reminderEngine;
        _dialogs = dialogs;
        _logger = logger;
        DataContext = entries;
        SettingsRoot.DataContext = settings;
        AiSettingsRoot.DataContext = aiSettings;
        GitContent.Content = new GitProjectsView(gitProjects);
        _weeklyView = new WeeklyReportsView(weeklyReports);
        WeeklyReportContent.Content = _weeklyView;
        BackupContent.Content = new BackupView(backup, CanStartImport, RefreshAfterImportAsync);
        _entries.DataChanged += (_, _) => _gitProjects.InvalidateForExternalChange();
        _backup.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(BackupViewModel.IsNavigationLocked))
            {
                MainTabs.IsEnabled = !_backup.IsNavigationLocked;
            }
        };
    }

    public async Task InitializeAsync()
    {
        var password = await _settings.LoadAsync();
        if (password is not null)
        {
            DatabasePassword.Password = password;
        }

        var apiKey = await _aiSettings.LoadAsync();
        if (apiKey is not null)
        {
            DeepSeekApiKey.Password = apiKey;
        }

        try
        {
            _autoStartEnabled = _autoStart.IsEnabled();
            AutoStartCheck.IsChecked = _autoStartEnabled;
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            _logger.LogWarning("Autostart settings failed: {ErrorType}", exception.GetType().Name);
            _dialogs.Error(ErrorMessages.Describe(exception), "自启动设置");
        }

        await _entries.RefreshAsync();
    }

    public bool CanExit()
    {
        if (_entries.IsBusy || _settings.IsBusy || _gitProjects.IsBusy || _aiSettings.IsBusy
            || _weeklyReports.IsBusy || _backup.IsBusy)
        {
            _dialogs.Error("数据库操作进行中，请等待完成后再退出。", "周报");
            return false;
        }

        return _entries.CanDiscardChanges() && _gitProjects.CanDiscardChanges() && _weeklyReports.CanDiscardChanges();
    }

    public void PrepareForExit() => _allowClose = true;

    public void SetReminderStatus(string message) => ReminderStatusText.Text = message;

    public async Task OpenWeekFromReminderAsync(DateOnly weekStart)
    {
        await WaitForBackupAsync();
        MainTabs.SelectedItem = WeeklyReportTab;
        await _weeklyView.EnsureInitializedAsync();
        await _weeklyReports.OpenWeekAsync(weekStart);
    }

    private Task WaitForBackupAsync()
    {
        if (!_backup.IsNavigationLocked)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(BackupViewModel.IsNavigationLocked) && !_backup.IsNavigationLocked)
            {
                _backup.PropertyChanged -= handler;
                completion.TrySetResult();
            }
        };
        _backup.PropertyChanged += handler;
        if (!_backup.IsNavigationLocked)
        {
            _backup.PropertyChanged -= handler;
            completion.TrySetResult();
        }

        return completion.Task;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            Hide();
        }
    }

    private void ToggleFloating_Click(object sender, RoutedEventArgs e) =>
        ((App)System.Windows.Application.Current).ToggleFloatingWindow();

    private async void TestReminder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _reminderNotifier.SendTestAsync(DateOnly.FromDateTime(DateTime.Today));
            SetReminderStatus("已向系统提交测试提醒；实际显示受系统通知和勿扰设置影响。");
        }
        catch (ReminderNotificationException exception)
        {
            _logger.LogWarning("Test reminder failed: {ErrorType}", exception.GetType().Name);
            _dialogs.Error(exception.Message, "测试提醒");
        }
    }

    private async void AcknowledgeReminder_Click(object sender, RoutedEventArgs e)
    {
        if (!_dialogs.Confirm(
            "请先手动检查周报。确认后将不再提示这些不确定的提醒，也不会自动重发。继续吗？", "确认提醒状态"))
        {
            return;
        }

        AcknowledgeReminderButton.IsEnabled = false;
        try
        {
            var count = await _reminderEngine.AcknowledgeUncertainAsync();
            var message = count == 0
                ? "没有需要确认的提醒状态。"
                : $"已知晓 {count} 条不确定的提醒状态；不会自动重发。";
            ((App)System.Windows.Application.Current).ReportReminderStatus(message);
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            _logger.LogWarning("Reminder acknowledgement failed: {ErrorType}", exception.GetType().Name);
            _dialogs.Error(ErrorMessages.Describe(exception), "确认提醒状态");
        }
        finally
        {
            AcknowledgeReminderButton.IsEnabled = true;
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e) =>
        await _settings.TestAsync(DatabasePassword.Password);

    private async void InitializeDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (await _settings.InitializeAndSaveAsync(DatabasePassword.Password))
        {
            await _entries.RefreshAsync();
            await _gitProjects.RefreshAsync();
            await _weeklyReports.RefreshHistoryAsync();
            await _backup.RefreshWeeksAsync();
        }
    }

    private bool CanStartImport()
    {
        if (_entries.IsBusy || _settings.IsBusy || _gitProjects.IsBusy || _aiSettings.IsBusy
            || _weeklyReports.IsBusy || _backup.IsBusy)
        {
            _dialogs.Error("其他操作进行中，请等待完成后再导入。", "导入备份");
            return false;
        }

        if (_entries.IsDirty || _gitProjects.IsProjectDirty || _gitProjects.IsWorkDirty || _weeklyReports.IsDirty)
        {
            _dialogs.Error("请先保存或放弃工作记录、项目及周报中未保存的修改，再导入备份。", "导入备份");
            return false;
        }

        return true;
    }

    private async Task RefreshAfterImportAsync()
    {
        MainTabs.IsEnabled = false;
        try
        {
            await _entries.RefreshAsync();
            await _gitProjects.RefreshAfterImportAsync();
            await _weeklyReports.RefreshAfterImportAsync();
            await _backup.RefreshAfterImportAsync();
        }
        finally
        {
            MainTabs.IsEnabled = true;
        }
    }

    private async void TestDeepSeek_Click(object sender, RoutedEventArgs e) =>
        await _aiSettings.TestAsync(DeepSeekApiKey.Password);

    private async void SaveDeepSeek_Click(object sender, RoutedEventArgs e) =>
        await _aiSettings.SaveAsync(DeepSeekApiKey.Password);

    private async void DeleteDeepSeek_Click(object sender, RoutedEventArgs e)
    {
        if (await _aiSettings.DeleteAsync())
        {
            DeepSeekApiKey.Clear();
        }
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var enabled = AutoStartCheck.IsChecked == true;
            _autoStart.SetEnabled(enabled);
            _autoStartEnabled = enabled;
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            AutoStartCheck.IsChecked = _autoStartEnabled;
            _logger.LogWarning("Autostart update failed: {ErrorType}", exception.GetType().Name);
            _dialogs.Error(ErrorMessages.Describe(exception), "自启动设置");
        }
    }
}
