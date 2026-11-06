using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Windows.Threading;
using WeeklyReport.App.Services;
using WeeklyReport.App.ViewModels;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Configuration;
using WeeklyReport.Infrastructure.Ai;
using WeeklyReport.Infrastructure.Backup;
using WeeklyReport.Infrastructure.Git;
using WeeklyReport.Infrastructure.Persistence;
// [新增-离线存储] Phase 1 切片 F：本地优先存储（SQLite）命名空间
using WeeklyReport.Infrastructure.Persistence.Sqlite;
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
    private ScheduledTaskMonitor? _scheduledTaskMonitor;
    private DispatcherTimer? _workRhythmTimer;
    private DateTimeOffset _lastWorkRhythmCheck;
    private WeeklyReportStatusProvider? _weeklyReportStatus;
    private IWeeklyProgressContextProvider? _weeklyProgressContext;
    private bool _ownsMutex;
    private int _exitStarted;

    // [新增-离线存储] Phase 1 切片 F：本地优先存储数据库路径（与配置/日志同目录的独立业务库，ADR-03 §4.1）
    public static string LocalDatabasePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeeklyReport", "weekly-report.db");

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

            // [新增-离线存储] Phase 1 切片 F：启动探测本地存储状态（local_meta 为事实来源），按模式路由仓储注册。
            // 损坏文件明确提示并以 MySQL 模式继续（数据仍在 MySQL，不伪装正常）。
            LocalStorageState? localState = null;
            if (File.Exists(LocalDatabasePath))
            {
                try
                {
                    localState = await new SqliteLocalStore(LocalDatabasePath).GetStateAsync();
                }
                catch (DatabaseAccessException)
                {
                    MessageBox.Show("本地离线存储文件不可用（可能损坏），本次以 MySQL 模式运行；可在设置中重建镜像。",
                        "周报", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
            }

            var localFirst = localState is { Status: LocalStorageStatus.Ready or LocalStorageStatus.Suspended };
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
            // [旧代码-备份离线化] 原固定 MySQL 备份实现（本地优先启用时由 SQLite 实现替代）
            // services.AddSingleton<IBackupStore, SqlSugarBackupStore>();
            // [新增-备份离线化] 切片 2：备份捕获/导入按模式路由——导入导出切到当前有效数据源
            services.AddSingleton<IBackupStore>(localFirst
                ? new SqliteBackupStore(LocalDatabasePath)
                : new SqlSugarBackupStore(new DatabaseConfigurationStore(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WeeklyReport", "database.json"))));
            services.AddSingleton<BackupImportPlanner>();
            services.AddSingleton<BackupService>();
            services.AddSingleton<IDatabaseInitializer, MySqlDatabaseInitializer>();

            // [旧代码-离线存储] 原固定 MySQL 仓储注册（本地优先启用时由 SQLite 实现替代）
            // services.AddSingleton<IWorkEntryRepository, SqlSugarWorkEntryRepository>();
            // services.AddSingleton<IGitProjectRepository, SqlSugarGitProjectRepository>();
            // services.AddSingleton<IGitSyncStore, SqlSugarGitSyncStore>();
            if (localFirst)
            {
                // [新增-离线存储] 本地优先模式：业务读写走 SQLite 有效视图（写路径自动入队，见各 SQLite 仓储）
                services.AddSingleton<IWorkEntryRepository>(_ => new SqliteWorkEntryRepository(LocalDatabasePath));
                services.AddSingleton<IGitProjectRepository>(_ => new SqliteGitProjectRepository(LocalDatabasePath));
                services.AddSingleton<IGitSyncStore>(_ => new SqliteGitSyncStore(LocalDatabasePath));
            }
            else
            {
                // [新增-离线存储] MySQL 模式（未启用本地优先或本地存储不可用）
                services.AddSingleton<IWorkEntryRepository, SqlSugarWorkEntryRepository>();
                services.AddSingleton<IGitProjectRepository, SqlSugarGitProjectRepository>();
                services.AddSingleton<IGitSyncStore, SqlSugarGitSyncStore>();
            }

            services.AddSingleton<IGitReportWorkItemSource>(provider => provider.GetRequiredService<IGitSyncStore>());
            services.AddSingleton<IGitCommitReader>(_ => new GitCliCommitReader());
            services.AddSingleton<GitHourEstimator>();
            services.AddSingleton<GitSyncPlanner>();
            services.AddSingleton<GitSyncService>();
            services.AddSingleton<IScheduledTaskSettingsStore>(provider =>
                new JsonScheduledTaskSettingsStore(
                    Path.Combine(dataDirectory, "scheduled-tasks.json"),
                    provider.GetRequiredService<ILogger<JsonScheduledTaskSettingsStore>>()));
            services.AddSingleton<ScheduledTaskScheduler>();
            services.AddSingleton<ScheduledGitSyncRunner>();
            services.AddSingleton<ScheduledWeeklyDraftRunner>();
            services.AddSingleton(provider => new ScheduledTaskMonitor(
                provider.GetRequiredService<IScheduledTaskSettingsStore>(),
                provider.GetRequiredService<ScheduledTaskScheduler>(),
                provider.GetRequiredService<ScheduledGitSyncRunner>(),
                provider.GetRequiredService<ScheduledWeeklyDraftRunner>(),
                provider.GetRequiredService<TimeProvider>(),
                ReportScheduledTaskResult,
                provider.GetRequiredService<ILogger<ScheduledTaskMonitor>>()));
            services.AddSingleton<WorkEntryService>();
            // [旧代码-离线存储] 原固定 MySQL 周报仓储注册（本地优先启用时由 SQLite 实现替代）
            // services.AddSingleton<IWeeklyReportRepository, SqlSugarWeeklyReportRepository>();
            // [新增-离线存储] Phase 1 切片 F：按模式路由（周选择列表与周报编辑同视图）
            services.AddSingleton<IWeeklyReportRepository>(localFirst
                ? new SqliteWeeklyReportRepository(LocalDatabasePath)
                : new SqlSugarWeeklyReportRepository(
                    new DatabaseConfigurationStore(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WeeklyReport", "database.json"))));
            // [新增-机器人交互] 本周周报保存状态查询（提醒文案区分已保存/未保存）
            services.AddSingleton<WeeklyReportStatusProvider>();
            services.AddSingleton<WeeklyReportDraftBuilder>();
            if (localFirst)
            {
                services.AddSingleton<ILocalStorageStateProvider>(
                    _ => new SqliteLocalStore(LocalDatabasePath));
            }

            services.AddSingleton<IWeeklyProgressContextProvider, WeeklyProgressContextProvider>();
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
            // [新增-机器人交互] 提醒事件与周报保存状态查询服务
            _weeklyReportStatus = _services.GetRequiredService<WeeklyReportStatusProvider>();
            _weeklyProgressContext = _services.GetRequiredService<IWeeklyProgressContextProvider>();

            _theme = new ThemeService(Resources, Dispatcher);
            _theme.Start();

            var mainWindow = _services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;

            // [新增-界面现代化2] 系统主题切换后同步刷新 Win11 标题栏底色
            _theme.ThemeChanged += () => WindowChromeHelper.ApplyCaptionColor(mainWindow);
            _floatingWindow = new FloatingWindow(
                Path.Combine(dataDirectory, "floating-window.json"),
                () => ShowMainWindow(),
                _services.GetRequiredService<ILogger<FloatingWindow>>());
            // [新增-机器人交互] 提醒气泡“打开周报”动作
            _floatingWindow.OpenWeekRequested += week => _ = OpenReminderWeekAsync(week);
            _floatingWindow.WeeklyProgressRequested += userInitiated =>
                _ = ShowWeeklyProgressAsync(userInitiated);
            _floatingWindow.AdviceActionRequested += (action, week) =>
                _ = NavigateFromWeeklyAdviceAsync(action, week);
            _floatingWindow.WorkRhythmReminderActionRequested += HandleWorkRhythmReminderAction;
            _tray = new TrayService(ShowMainWindow, ToggleFloatingWindow, ExitApplication);

            mainWindow.Show();
            _floatingWindow.Show();
            StartReminders();
            StartWorkRhythmReminders();
            // [新增-离线存储] G2：本地优先模式启动后台同步协调器
            await StartLocalSyncAsync();
            await mainWindow.InitializeAsync();
            await RefreshWeeklyProgressStatusAsync();
            _scheduledTaskMonitor = _services.GetRequiredService<ScheduledTaskMonitor>();
            _scheduledTaskMonitor.Start();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error("Application startup failed: {ErrorType}", exception.GetType().Name);
            MessageBox.Show("应用启动失败，请检查本地配置目录和运行环境。", "周报",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            _floatingWindow?.PrepareForApplicationShutdown();
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

    // [新增-离线存储] Phase 1 切片 F：设置页"离线可用"操作入口——查询状态 / 建立镜像 / 空工作区 / 停用。
    // 镜像读取 MySQL 侧经现有 MySQL 仓储（不走当前模式路由，避免本地优先已启用时读到 SQLite 自身）。
    public static async Task<LocalStorageState> GetLocalStoreStateAsync()
    {
        if (!File.Exists(LocalDatabasePath))
        {
            return new LocalStorageState(LocalStorageStatus.NotInitialized, false, 0, 0, 0, null, null);
        }

        return await new SqliteLocalStore(LocalDatabasePath).GetStateAsync();
    }

    public static async Task<string> BuildLocalMirrorAsync()
    {
        var configuration = new DatabaseConfigurationStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WeeklyReport", "database.json"));
        var settings = await configuration.LoadAsync()
            ?? throw new InvalidOperationException("尚未配置 MySQL，请先在设置中完成连接配置。");
        var snapshot = await new MySqlLocalMirrorSnapshotReader().ReadAsync(settings);
        var store = new SqliteLocalStore(LocalDatabasePath);
        await store.InitializeMirrorAsync(
            snapshot.Projects,
            snapshot.WorkItems,
            snapshot.WorkEntries,
            snapshot.Reports,
            mirrorIdentity: snapshot.Identity.DatabaseId);
        return "镜像建立完成，重启应用后以本地优先模式运行。";
    }

    public static async Task<string> StartEmptyLocalWorkspaceAsync()
    {
        await new SqliteLocalStore(LocalDatabasePath).StartEmptyWorkspaceAsync();
        return "空工作区已建立（无已验证基线），重启应用后以本地优先模式运行。";
    }

    public static async Task<string> DisableLocalFirstAsync()
    {
        await new SqliteLocalStore(LocalDatabasePath).DisableLocalFirstAsync();
        return "已停用本地优先，重启应用后以 MySQL 模式运行。";
    }

    // [新增-天线状态] 周报保存成功：请求悬浮精灵进入庆祝态（卡片态由 FloatingWindow 自行忽略）
    public void RequestFloatingCelebration()
    {
        // [新增-机器人交互] 保存成功后失效状态缓存，下一次提醒能读到最新保存状态
        _weeklyReportStatus?.Invalidate();
        _weeklyProgressContext?.Invalidate();
        _floatingWindow?.Celebrate();
    }

    public void InvalidateWeeklyProgressContext() => _weeklyProgressContext?.Invalidate();

    public async Task RefreshWeeklyProgressStatusAsync()
    {
        if (_weeklyProgressContext is null || _services is null)
        {
            return;
        }

        var context = await _weeklyProgressContext.GetCurrentWeekAsync();
        _services.GetRequiredService<MainWindow>().SetWeeklyProgressContext(context);
        _floatingWindow?.SetWeeklyProgressContext(context);
    }

    private async Task ShowWeeklyProgressAsync(bool userInitiated)
    {
        var provider = _weeklyProgressContext;
        if (provider is null || _services is null)
        {
            return;
        }

        WeeklyProgressContext context;
        try
        {
            context = await provider.GetCurrentWeekAsync(forceRefresh: userInitiated);
        }
        catch (Exception exception)
        {
            Log.Warning("Weekly progress retrieval failed: {ErrorType}", exception.GetType().Name);
            var weekStart = WeekPeriod.FromDate(
                DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime)).Start;
            context = new WeeklyProgressContext(
                weekStart, null, null, null, null, WeeklyStorageStatus.Unknown, 0, 0, null,
                exception.GetType().Name);
        }

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.SetWeeklyProgressContext(context);
        _floatingWindow?.SetWeeklyProgressContext(context);
        var advice = WeeklyAdvicePolicy.Create(context);
        if (_floatingWindow?.CurrentStyleValue == (int)FloatingStyle.Sprite)
        {
            _floatingWindow.ShowWeeklyAdvice(advice, context.WeekStart, userInitiated);
        }
        else if (userInitiated)
        {
            await mainWindow.NavigateToWeeklyAdviceAsync(advice.Action, context.WeekStart);
        }
    }

    private async Task NavigateFromWeeklyAdviceAsync(WeeklyAdviceAction action, DateOnly weekStart)
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            await _services.GetRequiredService<MainWindow>()
                .NavigateToWeeklyAdviceAsync(action, weekStart);
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            Log.Warning("Weekly advice navigation failed: {ErrorType}", exception.GetType().Name);
        }
    }

    // [新增-机器人交互] 设置页读取/保存精灵行为与预览问候
    public SpriteBehaviorSettings GetSpriteBehavior() =>
        _floatingWindow?.CurrentSpriteBehavior ?? SpriteBehaviorSettings.Default;

    public void SetSpriteBehavior(SpriteBehaviorSettings settings) =>
        _floatingWindow?.ApplySpriteBehavior(settings);

    public void PreviewSpriteGreeting() =>
        _floatingWindow?.PreviewGreeting();

    public WorkRhythmReminderSettings GetWorkRhythmSettings() =>
        _floatingWindow?.CurrentWorkRhythmSettings ?? WorkRhythmReminderSettings.Default;

    public void SetWorkRhythmEnabled(bool enabled)
    {
        var settings = GetWorkRhythmSettings() with { Enabled = enabled };
        _floatingWindow?.ApplyWorkRhythmSettings(settings);
    }

    // [新增-定时任务] 状态栏始终报告结果；气泡仅展示有意义的执行结果。
    private void ReportScheduledTaskResult(ScheduledTaskKind kind, ScheduledTaskOutcome outcome)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var taskName = kind == ScheduledTaskKind.GitDailyItems ? "Git 工作项同步" : "周报草稿生成";
            var status = $"{taskName}：{outcome.Message}";
            (MainWindow as MainWindow)?.SetReminderStatus(status);
            if (outcome.Kind != ScheduledTaskOutcomeKind.NoChanges)
            {
                _floatingWindow?.ShowScheduledTaskResult(
                    outcome.Message, outcome.Kind == ScheduledTaskOutcomeKind.Failed);
            }
        }));
    }

    // [新增-悬浮精灵] 切片 6：设置页悬浮窗样式读写入口（0=卡片，1=精灵）
    public int GetFloatingStyle() => _floatingWindow?.CurrentStyleValue ?? (int)FloatingStyle.Card;

    public void SetFloatingStyle(int style)
    {
        if (_floatingWindow is null)
        {
            return;
        }

        _floatingWindow.ApplyStyle((FloatingStyle)style);
    }

    // [旧代码-退出修复] 原同步退出实现：直接 Shutdown()，OnExit 中同步阻塞等待
    // ReminderMonitor.DisposeAsync 造成 UI 线程死锁（await 续体投递回被阻塞的 Dispatcher），
    // 表现为窗口关闭但进程残留并持有单实例互斥量。
    // private void ExitApplication()
    // {
    //     if (MainWindow is MainWindow mainWindow)
    //     {
    //         if (!mainWindow.CanExit())
    //         {
    //             return;
    //         }
    //
    //         mainWindow.PrepareForExit();
    //     }
    //
    //     _floatingWindow?.SavePosition();
    //     Shutdown();
    // }

    // [新增-退出修复] 托盘退出：先在 UI 线程空闲状态下异步释放提醒监视器（避免 OnExit 同步等待死锁），
    // 再启动退出看门狗并 Shutdown；监视器释放后置空，OnExit 不会重复释放。
    private async void ExitApplication()
    {
        if (MainWindow is MainWindow mainWindow)
        {
            if (!mainWindow.CanExit())
            {
                return;
            }
        }

        if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
        {
            return;
        }

        if (MainWindow is MainWindow window)
        {
            window.PrepareForExit();
        }

        _floatingWindow?.SavePosition();
        if (_reminderMonitor is not null)
        {
            try
            {
                await _reminderMonitor.DisposeAsync();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                Log.Warning("Reminder monitor disposal failed: {ErrorType}", exception.GetType().Name);
            }

            _reminderMonitor = null;
        }

        if (_scheduledTaskMonitor is not null)
        {
            try
            {
                await _scheduledTaskMonitor.DisposeAsync();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                Log.Warning("Scheduled task monitor disposal failed: {ErrorType}", exception.GetType().Name);
            }

            _scheduledTaskMonitor = null;
        }

        if (_syncCoordinator is not null)
        {
            try
            {
                await _syncCoordinator.DisposeAsync();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                Log.Warning("Local sync coordinator disposal failed: {ErrorType}", exception.GetType().Name);
            }

            _syncCoordinator = null;
        }

        _floatingWindow?.PrepareForApplicationShutdown();
        StartExitWatchdog();
        Shutdown();
    }

    // [新增-退出修复] 退出看门狗：后台线程 5 秒后若进程仍存活则记录日志并强制退出，
    // 兜底保证任何未预见的清理阻塞都不会让进程残留（后台线程在正常退出时随之终止）。
    private void StartExitWatchdog()
    {
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(5));
            Log.Warning("exit: watchdog forced exit after timeout");
            Log.CloseAndFlush();
            Environment.Exit(0);
        })
        {
            IsBackground = true,
            Name = "WeeklyReport.ExitWatchdog"
        };
        watchdog.Start();
    }

    // [新增-离线存储] G2：启动后台同步协调器（仅本地优先模式）
    private SqliteSyncCoordinator? _syncCoordinator;

    private async Task StartLocalSyncAsync()
    {
        if (!File.Exists(LocalDatabasePath))
        {
            return;
        }

        try
        {
            var state = await new SqliteLocalStore(LocalDatabasePath).GetStateAsync();
            if (state.Status is not (LocalStorageStatus.Ready or LocalStorageStatus.Suspended))
            {
                return;
            }

            _syncCoordinator = new SqliteSyncCoordinator(
                LocalDatabasePath,
                new DatabaseConfigurationStore(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WeeklyReport", "database.json")),
                TimeProvider.System,
                ReportLocalSyncStatus,
                _services?.GetRequiredService<ILogger<SqliteSyncCoordinator>>());
            _syncCoordinator.Start();
            Log.Information("Local sync coordinator started (pending: {Pending})", state.PendingCount);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or DatabaseAccessException)
        {
            Log.Warning("Local sync coordinator failed to start: {ErrorType}", exception.GetType().Name);
        }
    }

    private void ReportLocalSyncStatus(string message)
    {
        ReportReminderStatus(message);
    }

    /// <summary>手动"立即同步"（设置页按钮）。</summary>
    public async Task<SyncOutcome> SyncNowAsync()
    {
        if (_syncCoordinator is null)
        {
            return new SyncOutcome(0, 0, 0, "本地优先未启用或协调器未运行。");
        }

        return await _syncCoordinator.SyncNowAsync();
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
            // [新增-机器人交互] 订阅实际发送成功的提醒事件
            _reminderMonitor.ReminderSent += OnReminderSent;
            _reminderMonitor.Start();
        }
        catch (Exception exception) when (exception is ReminderNotificationException or InvalidOperationException)
        {
            Log.Warning("Reminder startup failed: {ErrorType}", exception.GetType().Name);
            ReportReminderStatus("系统提醒无法启动，请检查 Windows 通知权限和运行环境，修复后重新启动应用。");
        }
    }

    private void StartWorkRhythmReminders()
    {
        _reminderNotifier ??= _services?.GetRequiredService<WindowsReminderNotifier>();
        if (_reminderNotifier is not null)
        {
            _reminderNotifier.WorkRhythmActivated += OnWorkRhythmReminderActivated;
        }

        _lastWorkRhythmCheck = DateTimeOffset.Now;
        _workRhythmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _workRhythmTimer.Tick += OnWorkRhythmTimerTick;
        _workRhythmTimer.Start();
    }

    private async void OnWorkRhythmTimerTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        var previousCheck = _lastWorkRhythmCheck;
        _lastWorkRhythmCheck = now;
        var window = _floatingWindow;
        if (window is null)
        {
            return;
        }

        var settings = window.CurrentWorkRhythmSettings;
        var dueTime = WorkRhythmReminderPolicy.GetDueTime(previousCheck, now, settings);
        if (dueTime is { } scheduledTime)
        {
            settings = settings with { SnoozedUntil = null, LastReminderAt = scheduledTime };
            window.ApplyWorkRhythmSettings(settings);
            if (!window.ShowWorkRhythmReminder())
            {
                await SendWorkRhythmSystemNotificationAsync();
            }
        }
        else if (settings.SnoozedUntil is { } snoozedUntil && snoozedUntil <= now)
        {
            window.ApplyWorkRhythmSettings(settings with { SnoozedUntil = null });
        }
    }

    private void HandleWorkRhythmReminderAction(WorkRhythmReminderAction action)
    {
        var window = _floatingWindow;
        if (window is null)
        {
            return;
        }

        var settings = window.CurrentWorkRhythmSettings;
        if (action == WorkRhythmReminderAction.Acknowledge)
        {
            window.ApplyWorkRhythmSettings(settings with { SnoozedUntil = null });
            return;
        }

        var now = DateTimeOffset.Now;
        if (!settings.Enabled
            || settings.LastReminderAt is not { } lastReminderAt
            || now - lastReminderAt > TimeSpan.FromMinutes(30)
            || !WorkRhythmReminderPolicy.IsWorkingTime(now))
        {
            return;
        }

        window.ApplyWorkRhythmSettings(settings with { SnoozedUntil = now.AddMinutes(10) });
    }

    private void OnWorkRhythmReminderActivated(WorkRhythmReminderAction action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => HandleWorkRhythmReminderAction(action)));
    }

    private async Task SendWorkRhythmSystemNotificationAsync()
    {
        if (_reminderNotifier is null)
        {
            ReportReminderStatus("工作节律提醒无法显示：系统通知尚未注册。");
            return;
        }

        try
        {
            await _reminderNotifier.SendWorkRhythmReminderAsync();
        }
        catch (ReminderNotificationException exception)
        {
            Log.Warning("Work rhythm notification failed: {ErrorType}", exception.GetType().Name);
            ReportReminderStatus(exception.Message);
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

        // [旧代码-机器人交互] 原系统通知点击的内联打开逻辑，已提取为 OpenReminderWeekAsync 供气泡动作复用
        // Dispatcher.BeginInvoke(new Action(async () =>
        // {
        //     try
        //     {
        //         ShowMainWindow();
        //         if (MainWindow is MainWindow mainWindow)
        //         {
        //             await mainWindow.OpenWeekFromReminderAsync(weekStart);
        //         }
        //     }
        //     catch (Exception exception)
        //     {
        //         Log.Error("Reminder activation failed: {ErrorType}", exception.GetType().Name);
        //         MessageBox.Show("无法打开提醒对应的周报，请从主界面手动打开并检查日志。", "周报提醒",
        //             System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        //     }
        // }));

        // [新增-机器人交互] 系统通知与精灵气泡共用同一条打开周报路径
        Dispatcher.BeginInvoke(new Action(() => _ = OpenReminderWeekAsync(weekStart)));
    }

    // [新增-机器人交互] 打开提醒对应周；供系统通知点击与精灵气泡动作共用
    private async Task OpenReminderWeekAsync(DateOnly weekStart)
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
    }

    // [新增-机器人交互] 实际提醒事件：查询本周保存状态后交给悬浮精灵显示
    private void OnReminderSent(ReminderKey key)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var state = _weeklyReportStatus is null
                    ? WeeklyReportSavedState.Unknown
                    : await _weeklyReportStatus.GetWeekAsync(key.WeekStart);
                _floatingWindow?.ShowWeeklyReminder(key.WeekStart, state);
            }
            catch (Exception exception)
            {
                Log.Warning("Sprite reminder display failed: {ErrorType}", exception.GetType().Name);
            }
        }));
    }

    // [新增-机器人交互] 测试提醒按钮：查询状态并显示精灵提醒（不触发定时提醒去重）
    public async Task PreviewReminderAsync(DateOnly weekStart)
    {
        WeeklyReportSavedState state;
        try
        {
            state = _weeklyReportStatus is null
                ? WeeklyReportSavedState.Unknown
                : await _weeklyReportStatus.GetWeekAsync(weekStart);
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            Log.Warning("Reminder preview status failed: {ErrorType}", exception.GetType().Name);
            state = WeeklyReportSavedState.Unknown;
        }

        _floatingWindow?.ShowWeeklyReminder(weekStart, state);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _workRhythmTimer?.Stop();
        _workRhythmTimer = null;

        if (_scheduledTaskMonitor is not null)
        {
            var disposal = _scheduledTaskMonitor.DisposeAsync().AsTask();
            if (!disposal.Wait(TimeSpan.FromSeconds(3)))
            {
                Log.Warning("exit: scheduled task monitor disposal timed out, continuing shutdown");
            }
        }

        if (_reminderMonitor is not null)
        {
            // [旧代码-退出修复] 同步阻塞等待异步释放，UI 线程死锁根因（诊断日志已确认停在 monitor dispose begin）
            // _reminderMonitor.DisposeAsync().AsTask().GetAwaiter().GetResult();

            // [新增-退出修复] 兜底路径（如系统会话结束直接触发退出）：限时等待而非无限阻塞，
            // 超时记录警告继续退出；托盘路径已在 ExitApplication 中完成异步释放并置空。
            var disposal = _reminderMonitor.DisposeAsync().AsTask();
            if (!disposal.Wait(TimeSpan.FromSeconds(3)))
            {
                Log.Warning("exit: reminder monitor disposal timed out, continuing shutdown");
            }
        }

        if (_syncCoordinator is not null)
        {
            var disposal = _syncCoordinator.DisposeAsync().AsTask();
            if (!disposal.Wait(TimeSpan.FromSeconds(3)))
            {
                Log.Warning("exit: local sync coordinator disposal timed out, continuing shutdown");
            }
        }

        if (_reminderNotifier is not null)
        {
            _reminderNotifier.Activated -= OnReminderActivated;
            _reminderNotifier.WorkRhythmActivated -= OnWorkRhythmReminderActivated;
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

    protected override void OnSessionEnding(System.Windows.SessionEndingCancelEventArgs e)
    {
        _floatingWindow?.PrepareForApplicationShutdown();
        base.OnSessionEnding(e);
    }
}
