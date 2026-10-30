using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using WeeklyReport.App.Services;
using WeeklyReport.App.ViewModels;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Configuration;
using WeeklyReport.Infrastructure.Ai;
using WeeklyReport.Infrastructure.Backup;
using WeeklyReport.Infrastructure.Git;
using WeeklyReport.Infrastructure.Persistence;
using MessageBox = System.Windows.MessageBox;

namespace WeeklyReport.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private ServiceProvider? _services;
    private ThemeService? _theme;
    private TrayService? _tray;
    private FloatingWindow? _floatingWindow;
    private WindowsReminderNotifier? _reminderNotifier;
    private ReminderMonitor? _reminderMonitor;
    private bool _ownsMutex;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        var removingNotifications = (e.Args.Length == 1 || e.Args.Length == 2)
            && string.Equals(e.Args[0], "--unregister-notifications", StringComparison.Ordinal)
            && (e.Args.Length == 1 || string.Equals(e.Args[1], "--quiet", StringComparison.Ordinal));
        var quietMaintenance = removingNotifications && e.Args.Length == 2;
        _instanceMutex = new Mutex(true, @"Local\WeeklyReport.Desktop", out _ownsMutex);
        if (!_ownsMutex)
        {
            if (!quietMaintenance)
            {
                MessageBox.Show("周报应用已经在运行，请从系统托盘打开。", "周报");
            }
            Shutdown(removingNotifications ? 1 : 0);
            return;
        }

        if (removingNotifications)
        {
            try
            {
                await WindowsReminderNotifier.RemoveRegistrationAsync();
                Shutdown(0);
            }
            catch (ReminderNotificationException exception)
            {
                if (!quietMaintenance)
                {
                    MessageBox.Show(exception.Message, "周报提醒",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
                Shutdown(1);
            }

            return;
        }

        try
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeeklyReport");
            Directory.CreateDirectory(dataDirectory);
            var logDirectory = Path.Combine(dataDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(logDirectory, "weekly-report-.log"), rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14)
                .CreateLogger();

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.ClearProviders().AddSerilog(Log.Logger));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IReminderStateStore>(
                new ProtectedReminderStateStore(Path.Combine(dataDirectory, "reminders.json")));
            services.AddSingleton<ReminderSchedule>();
            services.AddSingleton<WindowsReminderNotifier>();
            services.AddSingleton<IReminderNotifier>(provider => provider.GetRequiredService<WindowsReminderNotifier>());
            services.AddSingleton<ReminderEngine>();
            services.AddSingleton<IDatabaseConfigurationStore>(
                new DatabaseConfigurationStore(Path.Combine(dataDirectory, "database.json")));
            services.AddSingleton<IAiSettingsStore>(
                new AiSettingsStore(Path.Combine(dataDirectory, "deepseek.json")));
            services.AddSingleton<IAiDraftService>(_ => new DeepSeekClient());
            services.AddSingleton<IAiReviewDialogs, AiReviewDialogs>();
            services.AddSingleton<IBackupReviewDialogs, BackupReviewDialogs>();
            services.AddSingleton<AiDraftCoordinator>();
            services.AddSingleton<JsonBackupCodec>();
            services.AddSingleton<ExcelBackupCodec>();
            services.AddSingleton<BackupFileService>();
            services.AddSingleton<IBackupStore, SqlSugarBackupStore>();
            services.AddSingleton<BackupImportPlanner>();
            services.AddSingleton<BackupService>();
            services.AddSingleton<IDatabaseInitializer, MySqlDatabaseInitializer>();
            services.AddSingleton<IWorkEntryRepository, SqlSugarWorkEntryRepository>();
            services.AddSingleton<IGitProjectRepository, SqlSugarGitProjectRepository>();
            services.AddSingleton<IGitSyncStore, SqlSugarGitSyncStore>();
            services.AddSingleton<IGitReportWorkItemSource>(provider => provider.GetRequiredService<IGitSyncStore>());
            services.AddSingleton<IGitCommitReader>(_ => new GitCliCommitReader());
            services.AddSingleton<GitHourEstimator>();
            services.AddSingleton<GitSyncPlanner>();
            services.AddSingleton<GitSyncService>();
            services.AddSingleton<WorkEntryService>();
            services.AddSingleton<IWeeklyReportRepository, SqlSugarWeeklyReportRepository>();
            services.AddSingleton<WeeklyReportDraftBuilder>();
            services.AddSingleton<WeeklyReportService>();
            services.AddSingleton<IUserDialogs, UserDialogs>();
            services.AddSingleton<WorkEntriesViewModel>();
            services.AddSingleton<DatabaseSettingsViewModel>();
            services.AddSingleton<GitProjectsViewModel>();
            services.AddSingleton<AiSettingsViewModel>();
            services.AddSingleton<WeeklyReportsViewModel>();
            services.AddSingleton<BackupViewModel>();
            services.AddSingleton<AutoStartService>();
            services.AddSingleton<MainWindow>();
            _services = services.BuildServiceProvider();

            _theme = new ThemeService(Resources, Dispatcher);
            _theme.Start();

            var mainWindow = _services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            _floatingWindow = new FloatingWindow(
                Path.Combine(dataDirectory, "floating-window.json"),
                () => ShowMainWindow(),
                _services.GetRequiredService<ILogger<FloatingWindow>>());
            _tray = new TrayService(ShowMainWindow, ToggleFloatingWindow, ExitApplication);

            mainWindow.Show();
            _floatingWindow.Show();
            StartReminders();
            await mainWindow.InitializeAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error("Application startup failed: {ErrorType}", exception.GetType().Name);
            MessageBox.Show("应用启动失败，请检查本地配置目录和运行环境。", "周报",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown();
        }
    }

    public void ShowMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        MainWindow.Show();
        MainWindow.WindowState = System.Windows.WindowState.Normal;
        MainWindow.Activate();
    }

    public void ToggleFloatingWindow()
    {
        if (_floatingWindow is null)
        {
            return;
        }

        if (_floatingWindow.IsVisible)
        {
            _floatingWindow.HideWithPosition();
        }
        else
        {
            _floatingWindow.Show();
            _floatingWindow.Activate();
        }
    }

    private void ExitApplication()
    {
        if (MainWindow is MainWindow mainWindow)
        {
            if (!mainWindow.CanExit())
            {
                return;
            }

            mainWindow.PrepareForExit();
        }

        _floatingWindow?.SavePosition();
        Shutdown();
    }

    private void StartReminders()
    {
        var services = _services ?? throw new InvalidOperationException("应用服务尚未初始化。");
        _reminderNotifier = services.GetRequiredService<WindowsReminderNotifier>();
        _reminderNotifier.Activated += OnReminderActivated;
        try
        {
            _reminderNotifier.Register();
            _reminderMonitor = new ReminderMonitor(
                services.GetRequiredService<ReminderEngine>(),
                services.GetRequiredService<ReminderSchedule>(),
                TimeProvider.System,
                ReportReminderStatus,
                services.GetRequiredService<ILogger<ReminderMonitor>>());
            _reminderMonitor.Start();
        }
        catch (Exception exception) when (exception is ReminderNotificationException or InvalidOperationException)
        {
            Log.Warning("Reminder startup failed: {ErrorType}", exception.GetType().Name);
            ReportReminderStatus("系统提醒无法启动，请检查 Windows 通知权限和运行环境，修复后重新启动应用。");
        }
    }

    internal void ReportReminderStatus(string message)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            (MainWindow as MainWindow)?.SetReminderStatus(message);
            _floatingWindow?.SetReminderStatus(message);
        }));
    }

    private void OnReminderActivated(DateOnly weekStart)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                ShowMainWindow();
                if (MainWindow is MainWindow mainWindow)
                {
                    await mainWindow.OpenWeekFromReminderAsync(weekStart);
                }
            }
            catch (Exception exception)
            {
                Log.Error("Reminder activation failed: {ErrorType}", exception.GetType().Name);
                MessageBox.Show("无法打开提醒对应的周报，请从主界面手动打开并检查日志。", "周报提醒",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }));
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_reminderMonitor is not null)
        {
            _reminderMonitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_reminderNotifier is not null)
        {
            _reminderNotifier.Activated -= OnReminderActivated;
            try
            {
                _reminderNotifier.Dispose();
            }
            catch (ReminderNotificationException exception)
            {
                Log.Warning("Reminder unregister failed: {ErrorType}", exception.GetType().Name);
                MessageBox.Show(exception.Message, "周报提醒",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        _tray?.Dispose();
        _theme?.Dispose();
        _services?.Dispose();
        if (_ownsMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }

        _instanceMutex?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
