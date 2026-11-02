// [新增-悬浮精灵] 精灵控件行为层：订阅状态机切换动画（浮动/眨眼/压扁/跳动/庆祝），
// 承载提醒气泡、右键菜单与悬停提示；拖动与打开主窗口经事件交由 FloatingWindow 执行。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WeeklyReport.App.Services;

namespace WeeklyReport.App.Controls;

// [新增-悬浮精灵] 完全限定 UserControl：项目全局 using 引入 WinForms 命名空间，避免与 Forms.UserControl 二义性
public partial class FloatingSprite : System.Windows.Controls.UserControl, IDisposable
{
    private readonly FloatingSpriteStateMachine _machine;
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _bubbleCloseTimer;
    private readonly Random _random = new();
    private string _weekText = string.Empty;
    private string _statusText = string.Empty;
    private bool _disposed;

    public FloatingSprite(FloatingSpriteStateMachine machine)
    {
        InitializeComponent();
        _machine = machine;

        _blinkTimer = new DispatcherTimer { Interval = NextBlinkInterval() };
        _blinkTimer.Tick += OnBlinkTimerTick;

        _bubbleCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _bubbleCloseTimer.Tick += (_, _) => CloseBubble();

        if (Resources["JumpStoryboard"] is Storyboard jumpStoryboard)
        {
            jumpStoryboard.Completed += (_, _) => StartIdleAnimations();
        }

        _machine.StateChanged += OnStateChanged;
        MouseLeftButtonDown += OnSpriteMouseLeftButtonDown;
        MouseLeftButtonUp += (_, _) => _machine.EndDrag();
        MouseLeave += (_, _) => _machine.EndDrag();
        MouseDoubleClick += (_, _) => OpenRequested?.Invoke();
        Loaded += (_, _) => ApplyState(_machine.State);
        Unloaded += (_, _) => StopContinuousAnimations();
    }

    /// <summary>精灵本体被按下（宿主窗口执行 DragMove 与位置保存）。</summary>
    public event Action? DragPressed;

    /// <summary>双击精灵或菜单"打开周报"。</summary>
    public event Action? OpenRequested;

    /// <summary>菜单"收起精灵"。</summary>
    public event Action? HideRequested;

    public void SetWeek(string text)
    {
        _weekText = text;
        UpdateToolTip();
    }

    public void SetReminderStatus(string message)
    {
        _statusText = message;
        UpdateToolTip();
        _machine.Notify();
    }

    /// <summary>宿主窗口隐藏时调用：收起气泡并复位状态，保证隐藏期间动画与气泡不残留。</summary>
    public void OnWindowHidden()
    {
        CloseBubble();
        _machine.Dismiss();
    }

    private void OnSpriteMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _machine.BeginDrag();
        DragPressed?.Invoke();
    }

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
                Play("CelebrateStoryboard");
                break;
        }
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
    }

    private void OnBlinkTimerTick(object? sender, EventArgs e)
    {
        _blinkTimer.Interval = NextBlinkInterval();
        if (IsVisible)
        {
            Play("BlinkStoryboard");
        }
    }

    private void OpenBubble()
    {
        BubbleText.Text = string.IsNullOrWhiteSpace(_statusText) ? "周报提醒" : _statusText;
        ReminderBubble.IsOpen = true;
        _bubbleCloseTimer.Stop();
        _bubbleCloseTimer.Start();
    }

    private void CloseBubble()
    {
        _bubbleCloseTimer.Stop();
        ReminderBubble.IsOpen = false;
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
        ReminderBubble.IsOpen = false;
    }
}
