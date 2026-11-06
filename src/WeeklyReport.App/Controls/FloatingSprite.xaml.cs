// [新增-悬浮精灵] 精灵控件行为层：订阅状态机切换动画（浮动/眨眼/压扁/跳动/庆祝），
// 承载提醒气泡、右键菜单与悬停提示；拖动与打开主窗口经事件交由 FloatingWindow 执行。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WeeklyReport.App.Services;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.App.Controls;

// [新增-悬浮精灵] 完全限定 UserControl：项目全局 using 引入 WinForms 命名空间，避免与 Forms.UserControl 二义性
public partial class FloatingSprite : System.Windows.Controls.UserControl, IDisposable
{
    private readonly FloatingSpriteStateMachine _machine;
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _bubbleCloseTimer;
    private readonly DispatcherTimer _pokeTimer;
    private readonly DispatcherTimer _faceResetTimer;
    private readonly DispatcherTimer _greetingTimer;
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _scenarioCueTimer;
    private readonly Random _random = new();
    private readonly Dictionary<string, DateTimeOffset> _scenarioCooldowns = new(StringComparer.Ordinal);
    private readonly List<ScenarioCue> _scenarioCues = [];
    private string _weekText = string.Empty;
    private string _statusText = string.Empty;
    private string _bubbleText = string.Empty;
    private string? _bubbleActionLabel;
    private DateOnly _reminderWeekStart;
    private DateOnly _adviceWeekStart;
    private WeeklyAdviceAction? _adviceAction;
    private SpriteBehaviorSettings _behavior = SpriteBehaviorSettings.Default;
    private System.Windows.Point _pressPosition;
    private DateTimeOffset _lastInteraction = DateTimeOffset.Now;
    private DateTimeOffset _lastChatter = DateTimeOffset.Now;
    private DateTimeOffset _lastDynamicAction = DateTimeOffset.Now;
    private DateTimeOffset _nextIdleAnimationAt = DateTimeOffset.Now;
    private DateTimeOffset _lastGlance = DateTimeOffset.Now;
    private DateTimeOffset _lastWeightShift = DateTimeOffset.Now;
    private DateTimeOffset? _mutedUntil;
    private DateTimeOffset _scenarioStartedAt;
    private Storyboard? _activeBodyAction;
    private EventHandler? _activeBodyActionCompleted;
    private Storyboard? _settleStoryboard;
    private EventHandler? _settleStoryboardCompleted;
    private SpriteActionPriority? _activeActionPriority;
    private SpriteActionPriority? _settlePriority;
    private FloatingSpriteState? _lastAppliedState;
    private string? _activeScenarioKey;
    private int _scenarioCueIndex;
    private bool _activeActionIsExtended;
    private bool _activeActionIsAutomatic;
    private bool _activeActionIsUserInitiated;
    private bool _activeActionRestoresFace;
    private bool _pressActive;
    private bool _isDragging;
    private bool _suppressClick;
    private bool _adviceRequestPending;
    private bool _isReminderBubble;
    private bool _isWorkRhythmReminderBubble;
    private bool _disposed;

    // [新增-机器人交互] 表情状态：正常、开心、困倦、惊讶、眩晕
    private enum FaceState { Normal, Happy, Sleepy, Surprised, Dizzy }
    private FaceState _face = FaceState.Normal;

    private sealed record ScenarioCue(TimeSpan At, string Text, TimeSpan Duration);

    private sealed record ScenarioAnimation(string StoryboardKey, TimeSpan Cooldown, ScenarioCue[] Cues);

    private static readonly ScenarioAnimation[] ScenarioPool =
    [
        new("GoOutStoryboard", TimeSpan.FromMinutes(8),
        [
            new(TimeSpan.Zero, "出去玩一圈～", TimeSpan.FromSeconds(3)),
            new(TimeSpan.FromSeconds(3.2), "回来啦！", TimeSpan.FromSeconds(2))
        ]),
        new("CarReturnStoryboard", TimeSpan.FromMinutes(8),
        [
            new(TimeSpan.FromSeconds(1.8), "坐车回来啦～ ٩(๑•̀ω•́๑)۶", TimeSpan.FromSeconds(3))
        ]),
        new("SpiralTakeoffStoryboard", TimeSpan.FromMinutes(10),
        [
            new(TimeSpan.FromSeconds(0.5), "起飞喽～", TimeSpan.FromSeconds(2)),
            new(TimeSpan.FromSeconds(4.4), "着陆成功！", TimeSpan.FromSeconds(2))
        ]),
        new("TakeNotesStoryboard", TimeSpan.FromMinutes(6),
        [
            new(TimeSpan.FromSeconds(0.8), "让我记一下...", TimeSpan.FromSeconds(3)),
            new(TimeSpan.FromSeconds(4.2), "记好了！✓", TimeSpan.FromSeconds(2))
        ])
    ];

    public FloatingSprite(FloatingSpriteStateMachine machine)
    {
        InitializeComponent();
        _machine = machine;

        _blinkTimer = new DispatcherTimer { Interval = NextBlinkInterval() };
        _blinkTimer.Tick += OnBlinkTimerTick;

        _bubbleCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _bubbleCloseTimer.Tick += (_, _) => CloseBubble();

        _pokeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pokeTimer.Tick += OnPokeTimerTick;

        _faceResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _faceResetTimer.Tick += (_, _) =>
        {
            _faceResetTimer.Stop();
            if (_machine.State == FloatingSpriteState.Idle)
            {
                SetFace(FaceState.Normal);
            }
        };

        _greetingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _greetingTimer.Tick += (_, _) =>
        {
            _greetingTimer.Stop();
            TryShowDailyGreeting();
        };

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _idleTimer.Tick += OnIdleTimerTick;

        _scenarioCueTimer = new DispatcherTimer();
        _scenarioCueTimer.Tick += OnScenarioCueTimerTick;

        _machine.StateChanged += OnStateChanged;
        MouseLeftButtonDown += OnSpriteMouseLeftButtonDown;
        // [旧代码-机器人交互] 原按下即拖动、单击无独立反馈
        // MouseLeftButtonUp += (_, _) => _machine.EndDrag();
        // MouseLeave += (_, _) => _machine.EndDrag();
        // MouseDoubleClick += (_, _) => OpenRequested?.Invoke();
        // Loaded += (_, _) => ApplyState(_machine.State);

        // [新增-机器人交互] 区分单击/拖动、悬停跟随与双击打开
        MouseLeftButtonUp += OnSpriteMouseLeftButtonUp;
        MouseMove += OnSpriteMouseMove;
        MouseLeave += OnSpriteMouseLeave;
        MouseDoubleClick += OnSpriteMouseDoubleClick;
        ContextMenuOpening += (_, _) => UpdateMuteMenuItem();
        Loaded += (_, _) =>
        {
            _lastInteraction = DateTimeOffset.Now;
            ApplyState(_machine.State);
            StartBehaviorTimers();
        };
        Unloaded += (_, _) =>
        {
            StopContinuousAnimations();
            StopAntennaAnimations();
        };
        Unloaded += (_, _) => StopBehaviorTimers();
    }

    /// <summary>精灵本体被按下（宿主窗口执行 DragMove 与位置保存）。</summary>
    public event Action? DragPressed;

    /// <summary>双击精灵或菜单"打开周报"。</summary>
    public event Action? OpenRequested;

    /// <summary>菜单"收起精灵"。</summary>
    public event Action? HideRequested;

    /// <summary>用户点击提醒气泡中的“打开周报”。</summary>
    public event Action<DateOnly>? OpenWeekRequested;

    public event Action<WorkRhythmReminderAction>? WorkRhythmReminderActionRequested;

    public event Action<bool>? WeeklyProgressRequested;

    public event Action<WeeklyAdviceAction, DateOnly>? AdviceActionRequested;

    public event Action<DateOnly>? AdviceShown;

    /// <summary>当天首次问候已显示，宿主据此持久化日期。</summary>
    public event Action<DateOnly>? GreetingShown;

    /// <summary>首次使用引导已显示，宿主据此持久化标记。</summary>
    public event Action? GuideShown;

    public void SetWeek(string text)
    {
        _weekText = text;
        UpdateToolTip();
    }

    public void SetReminderStatus(string message)
    {
        _statusText = message;
        UpdateToolTip();
        // [旧代码-机器人交互] 普通状态信息不再触发提醒动画
        // _machine.Notify();
    }

    // [新增-定时任务] 定时任务结果使用普通气泡；失败时仅触发柔和提醒动作。
    public void ShowScheduledTaskResult(string message, bool isFailure)
    {
        var now = DateTimeOffset.Now;
        if (_disposed || !IsVisible || !_behavior.ReminderBubble || ReminderBubble.IsOpen
            || IsMuted(now)
            || _behavior.QuietHours
                && SpriteMessagePolicy.IsQuietHours(TimeOnly.FromDateTime(now.DateTime)))
        {
            return;
        }

        InterruptBodyActionForMessage();
        _statusText = message;
        UpdateToolTip();
        ShowBubble(message, null, TimeSpan.FromSeconds(7));
        if (isFailure)
        {
            _machine.Notify(SpriteAlertKind.Soft);
        }
    }

    public void SetWeeklyProgressStatus(string message)
    {
        _statusText = message;
        UpdateToolTip();
    }

    // [新增-机器人交互] 保存设置并重启空闲计时；关闭悬停反应时立即复位眼神
    public void ApplyBehavior(SpriteBehaviorSettings settings)
    {
        var animationSettingsChanged = settings.DynamicActions != _behavior.DynamicActions
            || settings.AutoIdleActions != _behavior.AutoIdleActions;
        if (animationSettingsChanged)
        {
            _lastInteraction = DateTimeOffset.Now;
            _lastDynamicAction = _lastInteraction;
        }

        _behavior = settings;
        if ((!_behavior.DynamicActions && _activeActionIsExtended)
            || (!_behavior.AutoIdleActions && _activeActionIsAutomatic))
        {
            StopBodyAction(settle: false);
            if (_machine.State == FloatingSpriteState.Idle && IsVisible)
            {
                StartIdleAnimations();
            }
        }

        if (!_behavior.HoverReaction)
        {
            ResetHoverGaze();
        }

        if (IsLoaded && IsVisible)
        {
            _idleTimer.Start();
            _nextIdleAnimationAt = DateTimeOffset.Now
                + SpriteMessagePolicy.IdleAnimationInterval(_behavior.Preset);
            ResetAmbientActionTimes(DateTimeOffset.Now);
        }
    }

    // [新增-机器人交互] 宿主显示精灵时恢复问候/空闲计时
    public void OnWindowShown()
    {
        _lastInteraction = DateTimeOffset.Now;
        ApplyState(_machine.State);
        StartBehaviorTimers();
    }

    private void StartBehaviorTimers()
    {
        if (_disposed || !IsLoaded || !IsVisible)
        {
            return;
        }

        if (!_idleTimer.IsEnabled)
        {
            _idleTimer.Start();
        }

        if (!_greetingTimer.IsEnabled)
        {
            _greetingTimer.Interval = TimeSpan.FromSeconds(4);
            _greetingTimer.Start();
        }

        ResetAmbientActionTimes(DateTimeOffset.Now);
    }

    private void StopBehaviorTimers()
    {
        _greetingTimer.Stop();
        _idleTimer.Stop();
    }

    // [新增-机器人交互] 空闲扫描：偶尔东张西望，并按冷却判断是否闲聊
    private void OnIdleTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || !IsVisible)
        {
            return;
        }

        if (_activeBodyAction is not null || _settleStoryboard is not null)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        TryRequestIdleAdvice(now);
        TryShowIdleChatter(now);
        TryPlayIdleDance(now);
        TryPlayScenarioAnimation(now);

        TryPlayAmbientAction(now, "GlanceStoryboard", SpriteMessagePolicy.IdleGlanceInterval(_behavior.Preset),
            ref _lastGlance);
        TryPlayAmbientAction(now, "WeightShiftStoryboard",
            SpriteMessagePolicy.WeightShiftInterval(_behavior.Preset), ref _lastWeightShift);
    }

    private void TryPlayScenarioAnimation(DateTimeOffset now)
    {
        if (!_behavior.DynamicActions
            || _adviceRequestPending
            || _activeBodyAction is not null
            || _settleStoryboard is not null)
        {
            return;
        }

        var eligibleScenarios = ScenarioPool
            .Where(scenario =>
            {
                var sinceLastScenario = _scenarioCooldowns.TryGetValue(
                    scenario.StoryboardKey, out var lastPlayed)
                    ? now - lastPlayed
                    : scenario.Cooldown;
                return SpriteMessagePolicy.CanPlayAutomaticAmbient(
                    _behavior,
                    now,
                    now - _lastInteraction,
                    sinceLastScenario,
                    scenario.Cooldown,
                    IsVisible,
                    IsMouseOver,
                    ReminderBubble.IsOpen,
                    IsMuted(now),
                    _machine.State == FloatingSpriteState.Idle);
            })
            .ToArray();
        if (eligibleScenarios.Length == 0)
        {
            return;
        }

        var scenario = eligibleScenarios[_random.Next(eligibleScenarios.Length)];
        if (!StartBodyAction(
                scenario.StoryboardKey, extendedAction: true, SpriteActionPriority.Automatic,
                automatic: true, userInitiated: false))
        {
            return;
        }

        _scenarioCooldowns[scenario.StoryboardKey] = now;
        _lastDynamicAction = now;
        StartScenarioCues(scenario);
    }

    private void StartScenarioCues(ScenarioAnimation scenario)
    {
        StopScenarioCues();
        _activeScenarioKey = scenario.StoryboardKey;
        _scenarioStartedAt = DateTimeOffset.Now;
        _scenarioCues.AddRange(scenario.Cues);
        _scenarioCueIndex = 0;
        ShowDueScenarioCues();
        ScheduleNextScenarioCue();
    }

    private void OnScenarioCueTimerTick(object? sender, EventArgs e)
    {
        _scenarioCueTimer.Stop();
        if (_disposed || _activeScenarioKey is null || _activeBodyAction is null)
        {
            StopScenarioCues();
            return;
        }

        ShowDueScenarioCues();
        ScheduleNextScenarioCue();
    }

    private void ShowDueScenarioCues()
    {
        var elapsed = DateTimeOffset.Now - _scenarioStartedAt;
        while (_scenarioCueIndex < _scenarioCues.Count
               && _scenarioCues[_scenarioCueIndex].At <= elapsed)
        {
            var cue = _scenarioCues[_scenarioCueIndex];
            ShowBubble(cue.Text, null, cue.Duration);
            _scenarioCueIndex++;
        }
    }

    private void ScheduleNextScenarioCue()
    {
        if (_scenarioCueIndex >= _scenarioCues.Count)
        {
            return;
        }

        var remaining = _scenarioCues[_scenarioCueIndex].At - (DateTimeOffset.Now - _scenarioStartedAt);
        _scenarioCueTimer.Interval = remaining > TimeSpan.Zero
            ? remaining
            : TimeSpan.FromMilliseconds(1);
        _scenarioCueTimer.Start();
    }

    private void StopScenarioCues()
    {
        _scenarioCueTimer.Stop();
        _scenarioCues.Clear();
        _scenarioCueIndex = 0;
        _activeScenarioKey = null;
    }

    private void TryPlayIdleDance(DateTimeOffset now)
    {
        if (_adviceRequestPending
            || !SpriteMessagePolicy.CanPlayIdleDance(
                _behavior,
                now,
                now - _lastInteraction,
                now - _lastDynamicAction,
                IsVisible,
                IsMouseOver,
                ReminderBubble.IsOpen,
                IsMuted(now),
                _machine.State == FloatingSpriteState.Idle))
        {
            return;
        }

        if (StartBodyAction(
                "DanceStoryboard", extendedAction: true, SpriteActionPriority.Automatic,
                automatic: true, userInitiated: false))
        {
            _lastDynamicAction = now;
        }
    }

    private void TryPlayAmbientAction(
        DateTimeOffset now,
        string storyboardKey,
        TimeSpan interval,
        ref DateTimeOffset lastAction)
    {
        if (_activeBodyAction is not null
            || _settleStoryboard is not null
            || now - _lastDynamicAction < TimeSpan.FromSeconds(60)
            || !SpriteMessagePolicy.CanPlayAutomaticAmbient(
                _behavior,
                now,
                now - _lastInteraction,
                now - lastAction,
                interval,
                IsVisible,
                IsMouseOver,
                ReminderBubble.IsOpen,
                IsMuted(now),
                _machine.State == FloatingSpriteState.Idle))
        {
            return;
        }

        if (StartBodyAction(
                storyboardKey, extendedAction: false, SpriteActionPriority.Ambient,
                automatic: true, userInitiated: false))
        {
            lastAction = now;
            _lastDynamicAction = now;
        }
    }

    private void ResetAmbientActionTimes(DateTimeOffset now)
    {
        var glanceInterval = SpriteMessagePolicy.IdleGlanceInterval(_behavior.Preset);
        var weightShiftInterval = SpriteMessagePolicy.WeightShiftInterval(_behavior.Preset);
        _lastGlance = glanceInterval == Timeout.InfiniteTimeSpan ? now : now - glanceInterval;
        _lastWeightShift = weightShiftInterval == Timeout.InfiniteTimeSpan ? now : now - weightShiftInterval;
        _lastDynamicAction = now;
    }

    private void TryRequestIdleAdvice(DateTimeOffset now)
    {
        if (!WeeklyAdvicePolicy.CanShow(
                _behavior, now, now - _lastInteraction, IsVisible, ReminderBubble.IsOpen,
                IsMuted(now), userInitiated: false))
        {
            return;
        }

        RequestWeeklyProgress(userInitiated: false);
    }

    // [新增-机器人交互] 首次引导优先；否则每天最多一次时段问候
    private void TryShowDailyGreeting()
    {
        var now = DateTimeOffset.Now;
        if (!IsVisible || IsMuted(now) || ReminderBubble.IsOpen)
        {
            return;
        }

        var today = DateOnly.FromDateTime(now.DateTime);
        var weekStart = WeekPeriod.FromDate(today).Start;

        if (!_behavior.GuideShown)
        {
            var guide = SpriteMessagePolicy.CreateFirstRunGuide(_behavior, now);
            if (guide is not null)
            {
                _behavior = _behavior with { GuideShown = true, LastGreetingDate = today };
                ShowBubble(guide.Text, null, TimeSpan.FromSeconds(8));
                SetTransientFace(FaceState.Happy);
                GuideShown?.Invoke();
                GreetingShown?.Invoke(today);
                return;
            }
        }

        var greeting = SpriteMessagePolicy.CreateStartupGreeting(_behavior, now, weekStart);
        if (greeting is null)
        {
            return;
        }

        _behavior = _behavior with { LastGreetingDate = today };
        ShowBubble(greeting.Text, null, TimeSpan.FromSeconds(6));
        SetTransientFace(FaceState.Happy);
        GreetingShown?.Invoke(today);
    }

    // [新增-机器人交互] 空闲闲聊：尊重开关、夜间免打扰、气泡占用与冷却
    private void TryShowIdleChatter(DateTimeOffset now)
    {
        if (ReminderBubble.IsOpen || IsMuted(now) || !IsVisible || _adviceRequestPending)
        {
            return;
        }

        var message = SpriteMessagePolicy.CreateIdleChatter(
            _behavior, now, now - _lastInteraction, now - _lastChatter, _random);
        if (message is null)
        {
            return;
        }

        _lastChatter = now;
        ShowBubble(message.Text, null, TimeSpan.FromSeconds(6));
        SetTransientFace(FaceState.Happy);
    }

    // [新增-机器人交互] 保存成功庆祝气泡
    public void ShowCelebration()
    {
        var message = SpriteMessagePolicy.CreateCelebration(_behavior, _random);
        if (message is null)
        {
            return;
        }

        ShowBubble(message.Text, null, TimeSpan.FromSeconds(5));
    }

    // [新增-机器人交互] 周报提醒：未保存走橙色 Alerted + 动作气泡；已保存/未知走轻提示
    public void ShowWeeklyReminder(DateOnly weekStart, WeeklyReportSavedState state)
    {
        if (!_behavior.ReminderBubble)
        {
            return;
        }

        InterruptBodyActionForMessage();
        var message = SpriteMessagePolicy.CreateReminder(state, weekStart);
        _reminderWeekStart = weekStart;
        _statusText = message.Text;
        UpdateToolTip();

        if (message.ActionLabel is null)
        {
            ShowBubble(message.Text, null, TimeSpan.FromSeconds(6), isReminder: true);
            return;
        }

        // 先设置气泡文本与动作，再触发 Alerted；ApplyState(Alerted) 会复用已设置的气泡内容。
        ShowBubble(message.Text, message.ActionLabel, TimeSpan.FromSeconds(10), isReminder: true);
        if (state == WeeklyReportSavedState.NotSaved)
        {
            StopBodyAction(settle: false);
            _machine.Notify(SpriteAlertKind.Strong);
        }
    }

    public bool ShowWorkRhythmReminder()
    {
        if (_disposed || !IsVisible || !_behavior.ReminderBubble || ReminderBubble.IsOpen)
        {
            return false;
        }

        StopBodyAction(settle: false);
        _statusText = "工作节律提醒：喝点水，起来休息一下吧。";
        UpdateToolTip();
        ShowBubble(
            "工作一段时间啦，喝点水、起来活动一下吧。",
            "知道了",
            TimeSpan.FromSeconds(30),
            isReminder: true,
            isWorkRhythmReminder: true);
        _machine.Notify(SpriteAlertKind.Soft);
        return true;
    }

    // [新增-机器人交互] 设置页“打个招呼”预览：强制显示一条问候，不写每日问候日期
    public void PreviewGreeting()
    {
        var weekStart = WeekPeriod.FromDate(DateOnly.FromDateTime(DateTime.Today)).Start;
        var message = SpriteMessagePolicy.CreateGreeting(DateTimeOffset.Now, weekStart);
        InterruptBodyActionForMessage();
        ShowBubble(message.Text, null, TimeSpan.FromSeconds(6));
        SetTransientFace(FaceState.Happy);
    }

    public void ShowWeeklyAdvice(WeeklyAdvice advice, DateOnly weekStart, bool userInitiated)
    {
        ArgumentNullException.ThrowIfNull(advice);
        _adviceRequestPending = false;
        if (_disposed || !IsVisible || _isReminderBubble)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        if (!WeeklyAdvicePolicy.CanShow(
                _behavior, now, now - _lastInteraction, IsVisible, _isReminderBubble,
                IsMuted(now), userInitiated))
        {
            return;
        }

        var today = DateOnly.FromDateTime(now.DateTime);
        _adviceWeekStart = weekStart;
        InterruptBodyActionForMessage();
        ShowBubble(advice.Text, advice.ActionLabel, TimeSpan.FromSeconds(10));
        _adviceAction = advice.Action;
        if (!userInitiated)
        {
            _behavior = _behavior with { LastAdviceDate = today };
            AdviceShown?.Invoke(today);
        }
    }

    private void RequestWeeklyProgress(bool userInitiated)
    {
        if (_adviceRequestPending || WeeklyProgressRequested is null)
        {
            return;
        }

        _adviceRequestPending = true;
        WeeklyProgressRequested.Invoke(userInitiated);
    }

    /// <summary>宿主窗口隐藏时调用：收起气泡并复位状态，保证隐藏期间动画与气泡不残留。</summary>
    public void OnWindowHidden()
    {
        // [新增-机器人交互] 隐藏时停止问候/空闲计时，避免后台说话或动画
        StopBehaviorTimers();
        CloseBubble();
        _machine.Dismiss();
        StopContinuousAnimations();
        StopAntennaAnimations();
    }

    // [旧代码-机器人交互] 原按下即进入拖动
    // private void OnSpriteMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    // {
    //     _machine.BeginDrag();
    //     DragPressed?.Invoke();
    // }

    // [新增-机器人交互] 按下只记录位置；移动超过阈值才进入拖动，原地松开触发戳一戳
    private void OnSpriteMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _lastInteraction = DateTimeOffset.Now;
        _pressPosition = e.GetPosition(this);
        _pressActive = true;
        _isDragging = false;
        _suppressClick = false;
    }

    // [新增-机器人交互] 完全限定 MouseEventArgs：项目全局 using 引入 WinForms，避免二义性
    private void OnSpriteMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _lastInteraction = DateTimeOffset.Now;
        UpdateHoverGaze(e.GetPosition(BodyRoot));

        if (!_pressActive || _isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (!SpriteGesture.ExceedsDragThreshold(_pressPosition, e.GetPosition(this),
                SystemParameters.MinimumHorizontalDragDistance,
                SystemParameters.MinimumVerticalDragDistance))
        {
            return;
        }

        _isDragging = true;
        _pressActive = false;
        ResetHoverGaze();
        _machine.BeginDrag();
        DragPressed?.Invoke();
        // DragMove 返回时鼠标已松开，确保状态回落
        _isDragging = false;
        _machine.EndDrag();
    }

    private void OnSpriteMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_suppressClick)
        {
            _suppressClick = false;
            _pressActive = false;
            return;
        }

        if (_isDragging || !_pressActive)
        {
            _pressActive = false;
            _isDragging = false;
            _machine.EndDrag();
            return;
        }

        _pressActive = false;
        _pokeTimer.Stop();
        _pokeTimer.Start();
    }

    private void OnSpriteMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _suppressClick = true;
        _pokeTimer.Stop();
        _pressActive = false;
        OpenRequested?.Invoke();
    }

    private void OnSpriteMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _pressActive = false;
        _isDragging = false;
        _machine.EndDrag();
        _pokeTimer.Stop();
        ResetHoverGaze();
    }

    private void OnPokeTimerTick(object? sender, EventArgs e)
    {
        _pokeTimer.Stop();
        if (!_pressActive && _machine.State == FloatingSpriteState.Idle)
        {
            Poke();
        }
    }

    // [新增-机器人交互] 戳一戳：开心表情 + 回弹动画 + 可选短气泡
    private void Poke()
    {
        _lastInteraction = DateTimeOffset.Now;
        SetTransientFace(FaceState.Happy);
        var pokeStoryboard = _behavior.DynamicActions
            ? "PokeStoryboard"
            : "BasicPokeStoryboard";
        StartBodyAction(
            pokeStoryboard, extendedAction: _behavior.DynamicActions,
            SpriteActionPriority.User, userInitiated: true);
        _lastDynamicAction = _lastInteraction;

        var now = DateTimeOffset.Now;
        if (IsMuted(now))
        {
            return;
        }

        // 未保存提醒气泡优先，避免被戳一戳覆盖
        if (ReminderBubble.IsOpen && BubbleAction.Visibility == Visibility.Visible)
        {
            return;
        }

        var message = SpriteMessagePolicy.CreatePoke(_behavior, now, _random);
        if (message is not null)
        {
            ShowBubble(message.Text, null, TimeSpan.FromSeconds(4));
        }
    }

    // [新增-机器人交互] 临时表情在 1.2 秒后恢复；期间进入拖动/提醒/庆祝则不覆盖
    private void SetTransientFace(FaceState face)
    {
        SetFace(face);
        _faceResetTimer.Stop();
        _faceResetTimer.Start();
    }

    // [新增-机器人交互] 悬停眼神/身体轻倾；关闭悬停反应或非待机时复位
    private void UpdateHoverGaze(System.Windows.Point point)
    {
        if (_activeBodyAction is not null)
        {
            return;
        }

        if (!_behavior.HoverReaction || _machine.State != FloatingSpriteState.Idle)
        {
            ResetHoverGaze();
            return;
        }

        var x = Math.Clamp((point.X - 30) / 30.0, -1, 1) * 2;
        var y = Math.Clamp((point.Y - 35) / 35.0, -1, 1) * 1.2;
        LeftEyeLook.X = x;
        RightEyeLook.X = x;
        LeftEyeLook.Y = y;
        RightEyeLook.Y = y;
        BodyRotate.Angle = x * 1.5;
    }

    private void ResetHoverGaze()
    {
        LeftEyeLook.X = 0;
        LeftEyeLook.Y = 0;
        RightEyeLook.X = 0;
        RightEyeLook.Y = 0;
        BodyRotate.Angle = 0;
    }

    private bool IsMuted(DateTimeOffset now) =>
        _mutedUntil is { } until && now < until;

    private void MuteMenu_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.Now;
        _mutedUntil = _mutedUntil is { } until && now < until ? null : now.AddHours(1);
        UpdateMuteMenuItem();
    }

    private void UpdateMuteMenuItem()
    {
        if (_mutedUntil is { } until && DateTimeOffset.Now >= until)
        {
            _mutedUntil = null;
        }

        MuteMenuItem.Header = _mutedUntil is null ? "安静 1 小时" : "取消安静";
        WaveMenuItem.IsEnabled = _behavior.DynamicActions;
        DanceMenuItem.IsEnabled = _behavior.DynamicActions;
    }

    private void GreetMenu_Click(object sender, RoutedEventArgs e) => PreviewGreeting();

    // [新增-悬浮精灵] 状态机回调可能来自计时器线程，统一调度回 UI 线程再应用。
    private void OnStateChanged(FloatingSpriteState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => ApplyState(state)));
            return;
        }

        ApplyState(state);
    }

    private void ApplyState(FloatingSpriteState state)
    {
        if (_disposed)
        {
            return;
        }

        var previousState = _lastAppliedState;
        _lastAppliedState = state;
        if (state != FloatingSpriteState.Idle)
        {
            StopBodyAction(settle: false);
        }

        ApplyAntennaState(state);

        SetFace(state switch
        {
            FloatingSpriteState.Dragging => FaceState.Dizzy,
            FloatingSpriteState.Alerted when _machine.AlertKind == SpriteAlertKind.Strong => FaceState.Surprised,
            FloatingSpriteState.Celebrating => FaceState.Happy,
            _ => FaceState.Normal
        });

        switch (state)
        {
            case FloatingSpriteState.Idle:
                if (previousState == FloatingSpriteState.Dragging)
                {
                    StopBodyAction(settle: false, preserveCurrent: true);
                    StartBodyAction(
                        "DragSettleStoryboard", extendedAction: false, SpriteActionPriority.State,
                        userInitiated: false, preserveCurrent: true);
                }
                else if (previousState is not null and not FloatingSpriteState.Idle)
                {
                    SettleBodyAction(200, StartIdleAnimations);
                }
                else
                {
                    StartIdleAnimations();
                }
                break;
            case FloatingSpriteState.Dragging:
                StopContinuousAnimations();
                StartBodyAction(
                    "DragHoldStoryboard", extendedAction: false, SpriteActionPriority.State,
                    userInitiated: false);
                break;
            case FloatingSpriteState.Alerted:
                StopContinuousAnimations();
                OpenBubble();
                StartBodyAction(
                    _machine.AlertKind == SpriteAlertKind.Strong
                        ? "AlertStrongStoryboard"
                        : "AlertSoftStoryboard",
                    extendedAction: false, SpriteActionPriority.State, userInitiated: false);
                break;
            case FloatingSpriteState.Celebrating:
                StopContinuousAnimations();
                StartBodyAction(
                    "CelebrateStoryboard", extendedAction: false, SpriteActionPriority.Celebration,
                    userInitiated: false);
                break;
        }
    }

    private void ApplyAntennaState(FloatingSpriteState state)
    {
        StopAntennaAnimations();

        switch (state)
        {
            case FloatingSpriteState.Idle:
                SetAntennaFill("AccentBrush");
                break;
            case FloatingSpriteState.Dragging:
                SetAntennaFill("AccentBrush");
                break;
            case FloatingSpriteState.Alerted:
                SetAntennaFill(_machine.AlertKind == SpriteAlertKind.Strong
                    ? "AntennaAlertBrush"
                    : "AccentBrush");
                break;
            case FloatingSpriteState.Celebrating:
                SetAntennaFill("AntennaSuccessBrush");
                break;
        }
    }

    private void StopAntennaAnimations()
    {
        foreach (var key in new[]
                 {
                     "AntennaBreatheStoryboard", "AntennaAlertBlinkStoryboard", "AntennaWiggleStoryboard",
                     "IdleAntennaBreatheStoryboard"
                 })
        {
            if (Resources[key] is Storyboard storyboard)
            {
                storyboard.Stop(this);
            }
        }

        // [新增-天线状态] 显式摘除属性上的动画时钟：仅 Stop 在无限循环光环上可能残留最后一帧
        AntennaRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        AntennaBall.BeginAnimation(OpacityProperty, null);
        AntennaBallScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        AntennaBallScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        AntennaHalo.BeginAnimation(OpacityProperty, null);
        AntennaHaloScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        AntennaHaloScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);

        AntennaRotate.Angle = 0;
        AntennaBallScale.ScaleX = 1;
        AntennaBallScale.ScaleY = 1;
        AntennaBall.Opacity = 1;
        AntennaHalo.Opacity = 0;
        AntennaHaloScale.ScaleX = 1;
        AntennaHaloScale.ScaleY = 1;
    }

    private void SetAntennaFill(string resourceKey)
    {
        AntennaBall.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, resourceKey);
        // [旧代码-形象动画2] ChestLed 曾随天线换色，现由固定胸口核心替代。
        // ChestLed.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, resourceKey);
    }

    // [新增-机器人交互] 表情切换：正常/困倦/惊讶共用 LED 眼；开心/眩晕使用独立线条；
    // 切换时清除眨眼动画残留，眨眼只在正常表情下运行。
    private void SetFace(FaceState face)
    {
        _face = face;

        if (face != FaceState.Normal && Resources["BlinkStoryboard"] is Storyboard blink)
        {
            blink.Stop(this);
        }

        var normal = face is FaceState.Normal or FaceState.Sleepy or FaceState.Surprised;
        LeftEyeNormal.Visibility = normal ? Visibility.Visible : Visibility.Collapsed;
        RightEyeNormal.Visibility = normal ? Visibility.Visible : Visibility.Collapsed;
        LeftEyeHappy.Visibility = face == FaceState.Happy ? Visibility.Visible : Visibility.Collapsed;
        RightEyeHappy.Visibility = face == FaceState.Happy ? Visibility.Visible : Visibility.Collapsed;
        LeftEyeDizzy.Visibility = face == FaceState.Dizzy ? Visibility.Visible : Visibility.Collapsed;
        RightEyeDizzy.Visibility = face == FaceState.Dizzy ? Visibility.Visible : Visibility.Collapsed;

        MouthGrille.Visibility = face is FaceState.Normal or FaceState.Sleepy or FaceState.Dizzy
            ? Visibility.Visible
            : Visibility.Collapsed;
        MouthSmile.Visibility = face == FaceState.Happy ? Visibility.Visible : Visibility.Collapsed;
        MouthSurprised.Visibility = face == FaceState.Surprised ? Visibility.Visible : Visibility.Collapsed;

        var eyeScale = face switch
        {
            FaceState.Sleepy => 0.35,
            FaceState.Surprised => 1.25,
            _ => 1.0
        };

        LeftEyeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        LeftEyeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        RightEyeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        RightEyeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        LeftEyeScale.ScaleX = 1;
        LeftEyeScale.ScaleY = eyeScale;
        RightEyeScale.ScaleX = 1;
        RightEyeScale.ScaleY = eyeScale;
    }

    private void StartIdleAnimations()
    {
        if (_disposed || !IsLoaded || _activeBodyAction is not null)
        {
            return;
        }

        Play("IdleAntennaBreatheStoryboard");
        if (!_blinkTimer.IsEnabled)
        {
            _blinkTimer.Interval = NextBlinkInterval();
            _blinkTimer.Start();
        }
    }

    private void StopContinuousAnimations()
    {
        StopBodyAction(settle: false);
        if (Resources["IdleAntennaBreatheStoryboard"] is Storyboard antennaStoryboard)
        {
            antennaStoryboard.Stop(this);
            antennaStoryboard.Remove(this);
        }

        _blinkTimer.Stop();
        // [新增-机器人交互] 状态切换/卸载时停止戳一戳与临时表情计时器
        _pokeTimer.Stop();
        _faceResetTimer.Stop();
    }

    private void OnBlinkTimerTick(object? sender, EventArgs e)
    {
        _blinkTimer.Interval = NextBlinkInterval();
        // [旧代码-机器人交互] 原眨眼不区分表情
        // if (IsVisible)
        // {
        //     Play("BlinkStoryboard");
        // }

        // [新增-机器人交互] 只在正常表情下眨眼
        if (IsVisible && _face == FaceState.Normal)
        {
            Play("BlinkStoryboard");
        }
    }

    private void OpenBubble()
    {
        // [旧代码-机器人交互] 原标题直接取状态文本
        // BubbleText.Text = string.IsNullOrWhiteSpace(_statusText) ? "周报提醒" : _statusText;
        // ReminderBubble.IsOpen = true;
        // _bubbleCloseTimer.Stop();
        // _bubbleCloseTimer.Start();

        // [新增-机器人交互] 复用 ShowWeeklyReminder 已设置的气泡文本与动作
        ShowBubble(
            string.IsNullOrWhiteSpace(_bubbleText) ? "周报提醒" : _bubbleText,
            _bubbleActionLabel,
            _isWorkRhythmReminderBubble ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(10),
            isReminder: _isReminderBubble,
            isWorkRhythmReminder: _isWorkRhythmReminderBubble);
    }

    // [新增-机器人交互] 统一气泡显示：文本、可选动作按钮与非负时长
    private void ShowBubble(
        string text,
        string? actionLabel,
        TimeSpan duration,
        bool isReminder = false,
        bool isWorkRhythmReminder = false)
    {
        _bubbleText = text;
        _bubbleActionLabel = actionLabel;
        _adviceAction = null;
        _isReminderBubble = isReminder;
        _isWorkRhythmReminderBubble = isWorkRhythmReminder;
        BubbleText.Text = text;
        BubbleAction.Content = actionLabel ?? "打开周报";
        BubbleAction.Visibility = actionLabel is null ? Visibility.Collapsed : Visibility.Visible;
        BubbleSecondaryAction.Visibility = isWorkRhythmReminder ? Visibility.Visible : Visibility.Collapsed;
        ReminderBubble.IsOpen = true;
        _bubbleCloseTimer.Stop();
        _bubbleCloseTimer.Interval = duration;
        _bubbleCloseTimer.Start();
    }

    // [新增-机器人交互] 气泡动作：收起气泡并请求打开对应周
    private void BubbleAction_Click(object sender, RoutedEventArgs e)
    {
        if (_isWorkRhythmReminderBubble)
        {
            CloseBubble();
            WorkRhythmReminderActionRequested?.Invoke(WorkRhythmReminderAction.Acknowledge);
            return;
        }

        var adviceAction = _adviceAction;
        var adviceWeekStart = _adviceWeekStart;
        var reminderWeekStart = _reminderWeekStart;
        CloseBubble();
        if (adviceAction is WeeklyAdviceAction action)
        {
            AdviceActionRequested?.Invoke(action, adviceWeekStart);
        }
        else if (_bubbleActionLabel is not null)
        {
            OpenWeekRequested?.Invoke(reminderWeekStart);
        }
    }

    private void BubbleSecondaryAction_Click(object sender, RoutedEventArgs e)
    {
        if (!_isWorkRhythmReminderBubble)
        {
            return;
        }

        CloseBubble();
        WorkRhythmReminderActionRequested?.Invoke(WorkRhythmReminderAction.Snooze);
    }

    private void CloseBubble()
    {
        _bubbleCloseTimer.Stop();
        ReminderBubble.IsOpen = false;
        _isReminderBubble = false;
        _isWorkRhythmReminderBubble = false;
        _adviceAction = null;
    }

    private void UpdateToolTip()
    {
        var lines = new[] { _weekText, _statusText }.Where(static line => !string.IsNullOrWhiteSpace(line));
        ToolTip = string.Join(Environment.NewLine, lines);
    }

    private TimeSpan NextBlinkInterval() => TimeSpan.FromSeconds(3 + _random.NextDouble() * 3);

    private void Play(string storyboardKey)
    {
        if (Resources[storyboardKey] is Storyboard storyboard)
        {
            // [新增-悬浮精灵] Begin(this)：以本控件为名称范围解析 Storyboard.TargetName
            storyboard.Begin(this);
        }
    }

    private bool StartBodyAction(
        string storyboardKey,
        bool extendedAction,
        SpriteActionPriority priority,
        bool automatic = false,
        bool userInitiated = true,
        bool preserveCurrent = false,
        bool allowOpenBubble = false)
    {
        var stateAction = priority == SpriteActionPriority.State
            && (_machine.State != FloatingSpriteState.Idle || storyboardKey == "DragSettleStoryboard");
        var celebrationAction = priority == SpriteActionPriority.Celebration
            && _machine.State == FloatingSpriteState.Celebrating;
        if (_disposed
            || !IsLoaded
            || (extendedAction && !_behavior.DynamicActions)
            || (_machine.State != FloatingSpriteState.Idle && !stateAction && !celebrationAction)
            || (ReminderBubble.IsOpen && !stateAction && !celebrationAction && !allowOpenBubble)
            || Resources[storyboardKey] is not Storyboard storyboard)
        {
            return false;
        }

        if (_settleStoryboard is not null)
        {
            if (!stateAction && !celebrationAction)
            {
                return false;
            }

            StopSettleStoryboard(preserveCurrent: false);
        }

        if (_activeBodyAction is not null)
        {
            if (!SpriteActionPriorityRules.CanStart(_activeActionPriority, priority))
            {
                return false;
            }

            if (priority == SpriteActionPriority.User
                && _activeActionPriority == SpriteActionPriority.Ambient)
            {
                SettleBodyAction(200,
                    () => StartBodyAction(
                        storyboardKey, extendedAction, priority, automatic, userInitiated,
                        allowOpenBubble: true),
                    SpriteActionPriority.Ambient);
                return true;
            }

            StopBodyAction(settle: false);
        }

        ResetHoverGaze();
        if (Resources["IdleAntennaBreatheStoryboard"] is Storyboard breathe)
        {
            breathe.Stop(this);
            breathe.Remove(this);
        }

        _blinkTimer.Stop();
        if (!preserveCurrent)
        {
            ResetBodyActionTransforms();
        }

        _activeBodyAction = storyboard;
        _activeActionPriority = priority;
        _activeActionIsExtended = extendedAction;
        _activeActionIsAutomatic = automatic;
        _activeActionIsUserInitiated = userInitiated;
        _activeActionRestoresFace = automatic
            || storyboardKey is "WaveStoryboard" or "DanceStoryboard";
        if (automatic || storyboardKey is "WaveStoryboard" or "DanceStoryboard")
        {
            SetFace(FaceState.Happy);
        }

        EventHandler completed = (_, _) => CompleteBodyAction(storyboard);
        _activeBodyActionCompleted = completed;
        storyboard.Completed += completed;
        storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
        return true;
    }

    private void CompleteBodyAction(Storyboard storyboard)
    {
        if (!ReferenceEquals(_activeBodyAction, storyboard))
        {
            return;
        }

        if (_machine.State == FloatingSpriteState.Dragging
            && storyboard == Resources["DragHoldStoryboard"])
        {
            if (_activeActionPriority is null)
            {
                _activeActionPriority = SpriteActionPriority.State;
            }

            if (_activeBodyActionCompleted is { } dragCompleted)
            {
                storyboard.Completed -= dragCompleted;
            }
            _activeBodyActionCompleted = null;
            return;
        }

        if (_activeBodyActionCompleted is not null)
        {
            storyboard.Completed -= _activeBodyActionCompleted;
        }

        var priority = _activeActionPriority;
        var restoreFace = _activeActionRestoresFace;
        _activeBodyActionCompleted = null;
        _activeBodyAction = null;
        _activeActionPriority = null;
        storyboard.Remove(this);
        StopScenarioCues();
        ResetScenarioProps();
        _activeActionRestoresFace = false;
        _activeActionIsExtended = false;
        _activeActionIsAutomatic = false;
        _activeActionIsUserInitiated = false;
        if (storyboard == Resources["DragSettleStoryboard"])
        {
            ResetBodyActionTransforms();
            StartIdleAnimations();
            return;
        }

        if (_machine.State != FloatingSpriteState.Idle)
        {
            ResetBodyActionTransforms();
            return;
        }

        SettleBodyAction(200, () =>
        {
            if (restoreFace)
            {
                SetFace(FaceState.Normal);
            }

            if (_machine.State == FloatingSpriteState.Idle && IsVisible)
            {
                StartIdleAnimations();
            }
        }, priority);
    }

    private void StopBodyAction(bool settle = true, int settleMilliseconds = 200, bool preserveCurrent = false)
    {
        var snapshot = CaptureBodyTransforms();
        StopScenarioCues();
        ResetScenarioProps();
        var storyboard = _activeBodyAction;
        if (storyboard is not null)
        {
            if (_activeBodyActionCompleted is not null)
            {
                storyboard.Completed -= _activeBodyActionCompleted;
            }

            _activeBodyActionCompleted = null;
            _activeBodyAction = null;
            storyboard.Stop(this);
            storyboard.Remove(this);
        }

        var restoreFace = _activeActionRestoresFace;
        var priority = _activeActionPriority;
        _activeActionRestoresFace = false;
        _activeActionIsExtended = false;
        _activeActionIsAutomatic = false;
        _activeActionIsUserInitiated = false;
        _activeActionPriority = null;
        StopSettleStoryboard(preserveCurrent: true);

        if (settle)
        {
            ApplyBodyTransforms(snapshot);
            SettleBodyAction(settleMilliseconds, null, priority);
        }
        else if (preserveCurrent)
        {
            ApplyBodyTransforms(snapshot);
        }
        else
        {
            ResetBodyActionTransforms();
        }

        if (restoreFace && _machine.State == FloatingSpriteState.Idle)
        {
            SetFace(FaceState.Normal);
        }
    }

    private void SettleBodyAction(
        int milliseconds,
        Action? completed,
        SpriteActionPriority? priority = null)
    {
        var snapshot = CaptureBodyTransforms();
        StopSettleStoryboard(preserveCurrent: true);
        if (_activeBodyAction is { } active)
        {
            if (_activeBodyActionCompleted is not null)
            {
                active.Completed -= _activeBodyActionCompleted;
            }

            _activeBodyActionCompleted = null;
            _activeBodyAction = null;
            active.Stop(this);
            active.Remove(this);
        }

        _activeActionPriority = null;
        _activeActionRestoresFace = false;
        _activeActionIsExtended = false;
        _activeActionIsAutomatic = false;
        _activeActionIsUserInitiated = false;
        ApplyBodyTransforms(snapshot);
        _settlePriority = priority;

        var settle = new Storyboard();
        AddSettleAnimation(settle, BodyTranslate, System.Windows.Media.TranslateTransform.XProperty,
            snapshot.TranslateX, 0, milliseconds);
        AddSettleAnimation(settle, BodyTranslate, System.Windows.Media.TranslateTransform.YProperty,
            snapshot.TranslateY, 0, milliseconds);
        AddSettleAnimation(settle, BodyScale, System.Windows.Media.ScaleTransform.ScaleXProperty,
            snapshot.ScaleX, 1, milliseconds);
        AddSettleAnimation(settle, BodyScale, System.Windows.Media.ScaleTransform.ScaleYProperty,
            snapshot.ScaleY, 1, milliseconds);
        AddSettleAnimation(settle, BodyRotate, System.Windows.Media.RotateTransform.AngleProperty,
            snapshot.BodyAngle, 0, milliseconds);
        AddSettleAnimation(settle, CharacterSpinRotate, System.Windows.Media.RotateTransform.AngleProperty,
            snapshot.CharacterSpinAngle, 0, milliseconds);
        AddSettleAnimation(settle, CharacterSpinScale, System.Windows.Media.ScaleTransform.ScaleXProperty,
            snapshot.CharacterSpinScaleX, 1, milliseconds);
        AddSettleAnimation(settle, CharacterSpinScale, System.Windows.Media.ScaleTransform.ScaleYProperty,
            snapshot.CharacterSpinScaleY, 1, milliseconds);
        AddSettleAnimation(settle, LeftArmRotate, System.Windows.Media.RotateTransform.AngleProperty,
            snapshot.LeftArmAngle, 16, milliseconds);
        AddSettleAnimation(settle, RightArmRotate, System.Windows.Media.RotateTransform.AngleProperty,
            snapshot.RightArmAngle, -16, milliseconds);
        AddSettleAnimation(settle, LeftElbowRotate, System.Windows.Media.RotateTransform.AngleProperty,
            snapshot.LeftElbowAngle, 8, milliseconds);
        AddSettleAnimation(settle, RightElbowRotate, System.Windows.Media.RotateTransform.AngleProperty,
            snapshot.RightElbowAngle, -8, milliseconds);
        AddSettleAnimation(settle, LeftLegRotate, System.Windows.Media.RotateTransform.AngleProperty,
            snapshot.LeftLegAngle, 0, milliseconds);
        AddSettleAnimation(settle, RightLegRotate, System.Windows.Media.RotateTransform.AngleProperty,
            snapshot.RightLegAngle, 0, milliseconds);
        AddSettleAnimation(settle, LeftFootTranslate, System.Windows.Media.TranslateTransform.YProperty,
            snapshot.LeftFootY, 0, milliseconds);
        AddSettleAnimation(settle, RightFootTranslate, System.Windows.Media.TranslateTransform.YProperty,
            snapshot.RightFootY, 0, milliseconds);
        AddSettleAnimation(settle, AntennaRotate, System.Windows.Media.RotateTransform.AngleProperty,
            snapshot.AntennaAngle, 0, milliseconds);
        AddSettleAnimation(settle, AntennaBallScale, System.Windows.Media.ScaleTransform.ScaleXProperty,
            snapshot.AntennaScaleX, 1, milliseconds);
        AddSettleAnimation(settle, AntennaBallScale, System.Windows.Media.ScaleTransform.ScaleYProperty,
            snapshot.AntennaScaleY, 1, milliseconds);
        AddSettleAnimation(settle, AntennaBall, System.Windows.UIElement.OpacityProperty,
            snapshot.AntennaOpacity, 1, milliseconds);
        AddSettleAnimation(settle, AntennaHalo, System.Windows.UIElement.OpacityProperty,
            snapshot.HaloOpacity, 0, milliseconds);
        AddSettleAnimation(settle, AntennaHaloScale, System.Windows.Media.ScaleTransform.ScaleXProperty,
            snapshot.HaloScaleX, 1, milliseconds);
        AddSettleAnimation(settle, AntennaHaloScale, System.Windows.Media.ScaleTransform.ScaleYProperty,
            snapshot.HaloScaleY, 1, milliseconds);
        AddSettleAnimation(settle, ShadowScale, System.Windows.Media.ScaleTransform.ScaleXProperty,
            snapshot.ShadowScaleX, 1, milliseconds);
        AddSettleAnimation(settle, ShadowScale, System.Windows.Media.ScaleTransform.ScaleYProperty,
            snapshot.ShadowScaleY, 1, milliseconds);
        AddSettleAnimation(settle, GroundShadow, System.Windows.UIElement.OpacityProperty,
            snapshot.ShadowOpacity, 0.16, milliseconds);

        _settleStoryboard = settle;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (!ReferenceEquals(_settleStoryboard, settle))
            {
                return;
            }

            settle.Completed -= handler;
            _settleStoryboardCompleted = null;
            _settleStoryboard = null;
            _settlePriority = null;
            settle.Remove(this);
            ResetBodyActionTransforms();
            completed?.Invoke();
        };
        _settleStoryboardCompleted = handler;
        settle.Completed += handler;
        settle.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private static void AddSettleAnimation(
        Storyboard storyboard,
        DependencyObject target,
        System.Windows.DependencyProperty property,
        double from,
        double to,
        int milliseconds)
    {
        if (Math.Abs(from - to) < 0.001)
        {
            return;
        }

        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private void StopSettleStoryboard(bool preserveCurrent)
    {
        var settle = _settleStoryboard;
        if (settle is null)
        {
            return;
        }

        var snapshot = CaptureBodyTransforms();
        if (_settleStoryboardCompleted is not null)
        {
            settle.Completed -= _settleStoryboardCompleted;
        }

        _settleStoryboardCompleted = null;
        _settleStoryboard = null;
        _settlePriority = null;
        settle.Stop(this);
        settle.Remove(this);
        if (preserveCurrent)
        {
            ApplyBodyTransforms(snapshot);
        }
    }

    private void InterruptBodyActionForMessage()
    {
        if (_activeBodyAction is null)
        {
            return;
        }

        StopBodyAction(settle: false);
    }

    private void ResetBodyActionTransforms()
    {
        BodyTranslate.X = 0;
        BodyTranslate.Y = 0;
        BodyScale.ScaleX = 1;
        BodyScale.ScaleY = 1;
        BodyRotate.Angle = 0;
        CharacterSpinRotate.Angle = 0;
        CharacterSpinScale.ScaleX = 1;
        CharacterSpinScale.ScaleY = 1;
        LeftArmRotate.Angle = 16;
        RightArmRotate.Angle = -16;
        LeftElbowRotate.Angle = 8;
        RightElbowRotate.Angle = -8;
        LeftLegRotate.Angle = 0;
        RightLegRotate.Angle = 0;
        LeftFootTranslate.Y = 0;
        RightFootTranslate.Y = 0;
        AntennaRotate.Angle = 0;
        AntennaBallScale.ScaleX = 1;
        AntennaBallScale.ScaleY = 1;
        AntennaBall.Opacity = 1;
        AntennaHalo.Opacity = 0;
        AntennaHaloScale.ScaleX = 1;
        AntennaHaloScale.ScaleY = 1;
        ShadowScale.ScaleX = 1;
        ShadowScale.ScaleY = 1;
        GroundShadow.Opacity = 0.16;
        ResetScenarioProps();
        ResetHoverGaze();
    }

    private void ResetScenarioProps()
    {
        CharacterRoot.Opacity = 1;
        ShadowTranslate.X = 0;
        DoorProp.Opacity = 0;
        DoorRotate.Angle = 0;
        CarProp.Opacity = 0;
        CarTranslate.X = 0;
        CarTranslate.Y = 0;
        FrontWheelRotate.Angle = 0;
        RearWheelRotate.Angle = 0;
        NotebookProp.Opacity = 0;
        NotebookTranslate.X = 0;
        NotebookTranslate.Y = 0;
    }

    private readonly record struct BodyTransformSnapshot(
        double TranslateX,
        double TranslateY,
        double ScaleX,
        double ScaleY,
        double BodyAngle,
        double CharacterSpinAngle,
        double CharacterSpinScaleX,
        double CharacterSpinScaleY,
        double LeftArmAngle,
        double RightArmAngle,
        double LeftElbowAngle,
        double RightElbowAngle,
        double LeftLegAngle,
        double RightLegAngle,
        double LeftFootY,
        double RightFootY,
        double AntennaAngle,
        double AntennaScaleX,
        double AntennaScaleY,
        double AntennaOpacity,
        double HaloOpacity,
        double HaloScaleX,
        double HaloScaleY,
        double ShadowScaleX,
        double ShadowScaleY,
        double ShadowOpacity);

    private BodyTransformSnapshot CaptureBodyTransforms() => new(
        BodyTranslate.X,
        BodyTranslate.Y,
        BodyScale.ScaleX,
        BodyScale.ScaleY,
        BodyRotate.Angle,
        CharacterSpinRotate.Angle,
        CharacterSpinScale.ScaleX,
        CharacterSpinScale.ScaleY,
        LeftArmRotate.Angle,
        RightArmRotate.Angle,
        LeftElbowRotate.Angle,
        RightElbowRotate.Angle,
        LeftLegRotate.Angle,
        RightLegRotate.Angle,
        LeftFootTranslate.Y,
        RightFootTranslate.Y,
        AntennaRotate.Angle,
        AntennaBallScale.ScaleX,
        AntennaBallScale.ScaleY,
        AntennaBall.Opacity,
        AntennaHalo.Opacity,
        AntennaHaloScale.ScaleX,
        AntennaHaloScale.ScaleY,
        ShadowScale.ScaleX,
        ShadowScale.ScaleY,
        GroundShadow.Opacity);

    private void ApplyBodyTransforms(BodyTransformSnapshot snapshot)
    {
        BodyTranslate.X = snapshot.TranslateX;
        BodyTranslate.Y = snapshot.TranslateY;
        BodyScale.ScaleX = snapshot.ScaleX;
        BodyScale.ScaleY = snapshot.ScaleY;
        BodyRotate.Angle = snapshot.BodyAngle;
        CharacterSpinRotate.Angle = snapshot.CharacterSpinAngle;
        CharacterSpinScale.ScaleX = snapshot.CharacterSpinScaleX;
        CharacterSpinScale.ScaleY = snapshot.CharacterSpinScaleY;
        LeftArmRotate.Angle = snapshot.LeftArmAngle;
        RightArmRotate.Angle = snapshot.RightArmAngle;
        LeftElbowRotate.Angle = snapshot.LeftElbowAngle;
        RightElbowRotate.Angle = snapshot.RightElbowAngle;
        LeftLegRotate.Angle = snapshot.LeftLegAngle;
        RightLegRotate.Angle = snapshot.RightLegAngle;
        LeftFootTranslate.Y = snapshot.LeftFootY;
        RightFootTranslate.Y = snapshot.RightFootY;
        AntennaRotate.Angle = snapshot.AntennaAngle;
        AntennaBallScale.ScaleX = snapshot.AntennaScaleX;
        AntennaBallScale.ScaleY = snapshot.AntennaScaleY;
        AntennaBall.Opacity = snapshot.AntennaOpacity;
        AntennaHalo.Opacity = snapshot.HaloOpacity;
        AntennaHaloScale.ScaleX = snapshot.HaloScaleX;
        AntennaHaloScale.ScaleY = snapshot.HaloScaleY;
        ShadowScale.ScaleX = snapshot.ShadowScaleX;
        ShadowScale.ScaleY = snapshot.ShadowScaleY;
        GroundShadow.Opacity = snapshot.ShadowOpacity;
    }

    private void OpenMenu_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();

    private void WaveMenu_Click(object sender, RoutedEventArgs e)
    {
        _lastInteraction = DateTimeOffset.Now;
        StartBodyAction(
            "WaveStoryboard", extendedAction: true, SpriteActionPriority.User, userInitiated: true);
    }

    private void DanceMenu_Click(object sender, RoutedEventArgs e)
    {
        _lastInteraction = DateTimeOffset.Now;
        if (StartBodyAction(
                "DanceStoryboard", extendedAction: true, SpriteActionPriority.User, userInitiated: true))
        {
            _lastDynamicAction = DateTimeOffset.Now;
        }
    }

    private void WeeklyProgressMenu_Click(object sender, RoutedEventArgs e) =>
        RequestWeeklyProgress(userInitiated: true);

    private void HideMenu_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _machine.StateChanged -= OnStateChanged;
        StopContinuousAnimations();
        _bubbleCloseTimer.Stop();
        _scenarioCueTimer.Stop();
        StopBehaviorTimers();
        // [新增-天线状态] 停止无限循环的天线动画，避免重建精灵后时钟残留
        StopAntennaAnimations();
        ReminderBubble.IsOpen = false;
    }
}
