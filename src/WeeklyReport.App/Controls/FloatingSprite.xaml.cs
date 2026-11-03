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
    private readonly Random _random = new();
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
    private DateTimeOffset _nextIdleAnimationAt = DateTimeOffset.Now;
    private DateTimeOffset? _mutedUntil;
    private bool _pressActive;
    private bool _isDragging;
    private bool _suppressClick;
    private bool _adviceRequestPending;
    private bool _isReminderBubble;
    private bool _disposed;

    // [新增-机器人交互] 表情状态：正常、开心、困倦、惊讶、眩晕
    private enum FaceState { Normal, Happy, Sleepy, Surprised, Dizzy }
    private FaceState _face = FaceState.Normal;

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

        if (Resources["JumpStoryboard"] is Storyboard jumpStoryboard)
        {
            jumpStoryboard.Completed += (_, _) => StartIdleAnimations();
        }

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
        Unloaded += (_, _) => StopContinuousAnimations();
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

    public void SetWeeklyProgressStatus(string message)
    {
        _statusText = message;
        UpdateToolTip();
    }

    // [新增-机器人交互] 保存设置并重启空闲计时；关闭悬停反应时立即复位眼神
    public void ApplyBehavior(SpriteBehaviorSettings settings)
    {
        _behavior = settings;
        if (!_behavior.HoverReaction)
        {
            ResetHoverGaze();
        }

        if (IsLoaded && IsVisible)
        {
            _idleTimer.Start();
            _nextIdleAnimationAt = DateTimeOffset.Now
                + SpriteMessagePolicy.IdleAnimationInterval(_behavior.Preset);
        }
    }

    // [新增-机器人交互] 宿主显示精灵时恢复问候/空闲计时
    public void OnWindowShown() => StartBehaviorTimers();

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

        _nextIdleAnimationAt = DateTimeOffset.Now
            + SpriteMessagePolicy.IdleAnimationInterval(_behavior.Preset);
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

        var now = DateTimeOffset.Now;
        if (!IsMuted(now) && !IsMouseOver && now >= _nextIdleAnimationAt)
        {
            Play("LookAroundStoryboard");
            _nextIdleAnimationAt = now
                + SpriteMessagePolicy.IdleAnimationInterval(_behavior.Preset);
        }

        TryRequestIdleAdvice(now);
        TryShowIdleChatter(now);
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
            _machine.Notify();
        }
    }

    // [新增-机器人交互] 设置页“打个招呼”预览：强制显示一条问候，不写每日问候日期
    public void PreviewGreeting()
    {
        var weekStart = WeekPeriod.FromDate(DateOnly.FromDateTime(DateTime.Today)).Start;
        var message = SpriteMessagePolicy.CreateGreeting(DateTimeOffset.Now, weekStart);
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
        Play("PokeStoryboard");

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

        // [新增-天线状态] 先统一换装天线（颜色 + 动画），再执行原有身体动画
        ApplyAntennaState(state);

        // [新增-机器人交互] 状态对应表情：待机正常、拖动眩晕、提醒惊讶、庆祝开心
        SetFace(state switch
        {
            FloatingSpriteState.Dragging => FaceState.Dizzy,
            FloatingSpriteState.Alerted => FaceState.Surprised,
            FloatingSpriteState.Celebrating => FaceState.Happy,
            _ => FaceState.Normal
        });

        switch (state)
        {
            case FloatingSpriteState.Idle:
                Play("UnsquashStoryboard");
                StartIdleAnimations();
                break;
            case FloatingSpriteState.Dragging:
                StopContinuousAnimations();
                Play("SquashStoryboard");
                break;
            case FloatingSpriteState.Alerted:
                StopContinuousAnimations();
                Play("JumpStoryboard");
                OpenBubble();
                break;
            case FloatingSpriteState.Celebrating:
                StopContinuousAnimations();
                // [旧代码-天线状态] 原徽章放大庆祝；徽章已移除，庆祝动画由 ApplyAntennaState 的天线摆动承担
                // Play("CelebrateStoryboard");
                break;
        }
    }

    // [新增-天线状态] 状态到天线颜色/动画的映射：
    // Idle 强调色呼吸、Dragging 前倾 15° 且球缩至 0.8、Alerted 橙色快闪 + 光环、Celebrating 绿色摆动
    private void ApplyAntennaState(FloatingSpriteState state)
    {
        StopAntennaAnimations();

        switch (state)
        {
            case FloatingSpriteState.Idle:
                SetAntennaFill("AccentBrush");
                Play("AntennaBreatheStoryboard");
                break;
            case FloatingSpriteState.Dragging:
                SetAntennaFill("AccentBrush");
                AntennaRotate.Angle = 15;
                AntennaBallScale.ScaleX = 0.8;
                AntennaBallScale.ScaleY = 0.8;
                break;
            case FloatingSpriteState.Alerted:
                SetAntennaFill("AntennaAlertBrush");
                Play("AntennaAlertBlinkStoryboard");
                break;
            case FloatingSpriteState.Celebrating:
                SetAntennaFill("AntennaSuccessBrush");
                Play("AntennaWiggleStoryboard");
                break;
        }
    }

    // [新增-天线状态] 停止天线动画并复位基础值，保证状态切换从干净状态开始
    private void StopAntennaAnimations()
    {
        foreach (var key in new[]
                 {
                     "AntennaBreatheStoryboard", "AntennaAlertBlinkStoryboard", "AntennaWiggleStoryboard"
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

    // [旧代码-机器人交互] 原仅更新天线球颜色
    // private void SetAntennaFill(string resourceKey) =>
    //     AntennaBall.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, resourceKey);

    // [新增-机器人交互] 天线球与胸口状态灯同步换色
    private void SetAntennaFill(string resourceKey)
    {
        AntennaBall.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, resourceKey);
        ChestLed.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, resourceKey);
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
        if (_disposed || !IsLoaded)
        {
            return;
        }

        Play("BobStoryboard");
        if (!_blinkTimer.IsEnabled)
        {
            _blinkTimer.Interval = NextBlinkInterval();
            _blinkTimer.Start();
        }
    }

    private void StopContinuousAnimations()
    {
        if (Resources["BobStoryboard"] is Storyboard bobStoryboard)
        {
            bobStoryboard.Stop(this);
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
            TimeSpan.FromSeconds(10),
            isReminder: _isReminderBubble);
    }

    // [新增-机器人交互] 统一气泡显示：文本、可选动作按钮与非负时长
    private void ShowBubble(string text, string? actionLabel, TimeSpan duration, bool isReminder = false)
    {
        _bubbleText = text;
        _bubbleActionLabel = actionLabel;
        _adviceAction = null;
        _isReminderBubble = isReminder;
        BubbleText.Text = text;
        BubbleAction.Content = actionLabel ?? "打开周报";
        BubbleAction.Visibility = actionLabel is null ? Visibility.Collapsed : Visibility.Visible;
        ReminderBubble.IsOpen = true;
        _bubbleCloseTimer.Stop();
        _bubbleCloseTimer.Interval = duration;
        _bubbleCloseTimer.Start();
    }

    // [新增-机器人交互] 气泡动作：收起气泡并请求打开对应周
    private void BubbleAction_Click(object sender, RoutedEventArgs e)
    {
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

    private void CloseBubble()
    {
        _bubbleCloseTimer.Stop();
        ReminderBubble.IsOpen = false;
        _isReminderBubble = false;
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

    private void OpenMenu_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();

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
        _blinkTimer.Stop();
        _bubbleCloseTimer.Stop();
        _pokeTimer.Stop();
        _faceResetTimer.Stop();
        StopBehaviorTimers();
        // [新增-天线状态] 停止无限循环的天线动画，避免重建精灵后时钟残留
        StopAntennaAnimations();
        ReminderBubble.IsOpen = false;
    }
}
