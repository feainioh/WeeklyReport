using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WeeklyReport.App.Services;
// [新增-悬浮精灵] 卡片与精灵内容控件所在命名空间
using WeeklyReport.App.Controls;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace WeeklyReport.App;

public partial class FloatingWindow : Window
{
    private readonly string _positionPath;
    private readonly Action _openMain;
    private readonly ILogger<FloatingWindow> _logger;
    private readonly DispatcherTimer _timer;

    // [新增-悬浮精灵] 样式偏好与内容控件：卡片（FloatingCard）或精灵（FloatingSprite）二选一宿主
    private readonly FloatingSpriteStateMachine _machine;
    private FloatingStyle _style;
    private FloatingCard? _card;
    private FloatingSprite? _sprite;
    private SpriteBehaviorSettings _spriteBehavior = SpriteBehaviorSettings.Default;
    private WorkRhythmReminderSettings _workRhythmSettings = WorkRhythmReminderSettings.Default;
    private string _weeklyProgressStatus = "存储状态：正在读取";

    // [新增-悬浮精灵] 精灵形态窗口尺寸（含跳动空间）；卡片形态沿用 320x165
    // [旧代码-机器人化] 原文档精灵尺寸 150x190，机器人化缩小 50% 后改为 75x95
    // private const double SpriteWidth = 150;
    // private const double SpriteHeight = 190;
    // [旧代码-舞蹈优化] 原尺寸 75x95，增高后改为 75x115（+20px 跳跃空间）
    // private const double SpriteWidth = 75;
    // private const double SpriteHeight = 95;
    // [新增-舞蹈优化] 精灵窗口尺寸：高度 95→115，给 -20px 大跳 + 天线 14px 空间
    private const double SpriteWidth = 75;
    private const double SpriteHeight = 115;

    public FloatingWindow(string positionPath, Action openMain, ILogger<FloatingWindow> logger)
    {
        InitializeComponent();
        _positionPath = positionPath;
        _openMain = openMain;
        _logger = logger;

        // [新增-悬浮精灵] 启动时读取样式偏好（旧文件缺 Style 字段时默认卡片），并按样式构建内容（切片 2/3）
        _machine = new FloatingSpriteStateMachine();
        // [旧代码-机器人交互] 原只读取样式
        // _style = FloatingWindowPreferencesStore.TryLoad(_positionPath)?.Style ?? FloatingStyle.Card;

        // [新增-机器人交互] 同时读取精灵行为设置
        var preferences = FloatingWindowPreferencesStore.TryLoad(_positionPath);
        _style = preferences?.Style ?? FloatingStyle.Card;
        _spriteBehavior = preferences?.Sprite ?? SpriteBehaviorSettings.Default;
        _workRhythmSettings = preferences?.WorkRhythm ?? WorkRhythmReminderSettings.Default;
        BuildContent();

        // [新增-机器人交互] 显示/隐藏时启停精灵问候与空闲计时
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _sprite?.OnWindowShown();
            }
            else
            {
                _sprite?.OnWindowHidden();
            }
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => UpdateWeek();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _timer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            // [新增-悬浮精灵] 释放精灵与状态机资源
            _sprite?.Dispose();
            _machine.Dispose();
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>当前样式值（0=卡片，1=精灵），供设置页初始化选中项。</summary>
    public int CurrentStyleValue => (int)_style;

    // [新增-悬浮精灵] 切片 6：设置页切换样式的运行时入口——重建内容、按需找回可见性并持久化（含新样式）
    public void ApplyStyle(FloatingStyle style)
    {
        if (_style == style)
        {
            return;
        }

        _style = style;
        BuildContent();
        if (!IsVisibleOnScreen())
        {
            MoveToPrimaryWorkArea();
        }

        SavePosition();
    }

    public void HideWithPosition()
    {
        // [新增-悬浮精灵] 隐藏前收起精灵气泡并复位状态，避免托盘恢复时残留
        _sprite?.OnWindowHidden();
        SavePosition();
        Hide();
    }

    public void SetReminderStatus(string message)
    {
        // [旧代码-悬浮精灵] 卡片文本直写逻辑，卡片元素已迁往 FloatingCard，现按样式路由
        // ReminderStatusText.Text = message;
        // ReminderStatusText.ToolTip = message;

        // [新增-悬浮精灵] 切片 5：按样式路由提醒状态（精灵侧触发跳动 + 气泡）
        if (_style == FloatingStyle.Sprite)
        {
            _sprite?.SetReminderStatus(message);
        }
        else
        {
            _card?.SetReminderStatus(message);
        }
    }

    // [新增-定时任务] 定时任务结果路由到主状态文案与悬浮精灵气泡。
    public void ShowScheduledTaskResult(string message, bool isFailure)
    {
        SetReminderStatus(message);
        if (_style == FloatingStyle.Sprite && IsVisible)
        {
            _sprite?.ShowScheduledTaskResult(message, isFailure);
        }
    }

    public void SetWeeklyProgressContext(WeeklyProgressContext context)
    {
        _weeklyProgressStatus = $"存储状态：{WeeklyProgressStatusFormatter.FormatStorage(context)}";
        _card?.SetStorageStatus(_weeklyProgressStatus);
        _sprite?.SetWeeklyProgressStatus(_weeklyProgressStatus);
    }

    public void ShowWeeklyAdvice(WeeklyAdvice advice, DateOnly weekStart, bool userInitiated)
    {
        if (_style == FloatingStyle.Sprite)
        {
            _sprite?.ShowWeeklyAdvice(advice, weekStart, userInitiated);
        }
    }

    // [新增-机器人交互] 当前精灵行为设置（设置页初始化读取）
    public SpriteBehaviorSettings CurrentSpriteBehavior => _spriteBehavior;

    // [新增-机器人交互] 用户点击提醒气泡中的“打开周报”
    public event Action<DateOnly>? OpenWeekRequested;

    public event Action<WorkRhythmReminderAction>? WorkRhythmReminderActionRequested;

    public event Action<bool>? WeeklyProgressRequested;

    public event Action<WeeklyAdviceAction, DateOnly>? AdviceActionRequested;

    // [新增-机器人交互] 设置页保存行为设置：立即应用并持久化
    public void ApplySpriteBehavior(SpriteBehaviorSettings settings)
    {
        _spriteBehavior = settings;
        _sprite?.ApplyBehavior(settings);
        SavePosition();
    }

    public WorkRhythmReminderSettings CurrentWorkRhythmSettings => _workRhythmSettings;

    public void ApplyWorkRhythmSettings(WorkRhythmReminderSettings settings)
    {
        _workRhythmSettings = settings;
        SavePosition();
    }

    public bool ShowWorkRhythmReminder()
    {
        if (_style != FloatingStyle.Sprite || !IsVisible)
        {
            return false;
        }

        return _sprite?.ShowWorkRhythmReminder() == true;
    }

    // [新增-机器人交互] 周报提醒路由：精灵走气泡/动画，卡片只更新文本
    public void ShowWeeklyReminder(DateOnly weekStart, WeeklyReportSavedState state)
    {
        if (_style == FloatingStyle.Sprite)
        {
            _sprite?.ShowWeeklyReminder(weekStart, state);
            return;
        }

        _card?.SetReminderStatus(SpriteMessagePolicy.CreateReminder(state, weekStart).Text);
    }

    // [新增-机器人交互] 设置页“打个招呼”预览
    public void PreviewGreeting()
    {
        if (_style == FloatingStyle.Sprite)
        {
            _sprite?.PreviewGreeting();
        }
    }

    // [新增-天线状态] 周报保存成功：仅精灵形态进入庆祝态，卡片形态忽略
    public void Celebrate()
    {
        if (_style == FloatingStyle.Sprite)
        {
            // [新增-机器人交互] 关闭庆祝开关时忽略
            if (!_spriteBehavior.Celebration)
            {
                return;
            }

            _machine.Celebrate();
            // [新增-机器人交互] 保存成功气泡
            _sprite?.ShowCelebration();
        }
    }

    public void SavePosition()
    {
        try
        {
            // [旧代码-悬浮精灵] 原内联原子写位置逻辑，改由 FloatingWindowPreferencesStore 统一承载（含样式字段）
            // var directory = Path.GetDirectoryName(Path.GetFullPath(_positionPath))
            //     ?? throw new InvalidOperationException("无法确定悬浮窗配置目录。");
            // Directory.CreateDirectory(directory);
            // var temporary = Path.Combine(directory, $".floating-{Guid.NewGuid():N}.tmp");
            // try
            // {
            //     File.WriteAllText(temporary, JsonSerializer.Serialize(new FloatingPosition(Left, Top)));
            //     File.Move(temporary, _positionPath, overwrite: true);
            // }
            // finally
            // {
            //     if (File.Exists(temporary))
            //     {
            //         File.Delete(temporary);
            //     }
            // }

            // [旧代码-机器人交互] 原偏好仅位置 + 样式
            // FloatingWindowPreferencesStore.Save(
            //     _positionPath, new FloatingWindowPreferences(Left, Top, _style));

            // [新增-机器人交互] 同时保存精灵行为设置
            FloatingWindowPreferencesStore.Save(
                _positionPath,
                new FloatingWindowPreferences(Left, Top, _style, _spriteBehavior, _workRhythmSettings));
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            _logger.LogWarning("Floating window position save failed: {ErrorType}", exception.GetType().Name);
            MessageBox.Show("悬浮窗位置保存失败。请检查本地配置目录。", "周报",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateWeek();
        _timer.Start();
        MoveToPrimaryWorkArea();

        try
        {
            // [旧代码-悬浮精灵] 原内联读取与校验逻辑，改由 FloatingWindowPreferencesStore.TryLoad 承载
            // if (!File.Exists(_positionPath))
            // {
            //     return;
            // }
            //
            // var position = JsonSerializer.Deserialize<FloatingPosition>(File.ReadAllText(_positionPath))
            //     ?? throw new InvalidDataException("悬浮窗位置配置为空。");
            // if (!double.IsFinite(position.Left) || !double.IsFinite(position.Top)
            //     || Math.Abs(position.Left) > 100000 || Math.Abs(position.Top) > 100000)
            // {
            //     throw new InvalidDataException("悬浮窗位置配置无效。");
            // }

            // [新增-悬浮精灵] 切片 2：读取位置 + 样式；文件存在但无效时保持原有提示行为
            if (!File.Exists(_positionPath))
            {
                return;
            }

            var preferences = FloatingWindowPreferencesStore.TryLoad(_positionPath);
            if (preferences is null)
            {
                throw new InvalidDataException("悬浮窗位置配置无效。");
            }

            var position = preferences;
            Left = position.Left;
            Top = position.Top;
            if (!IsVisibleOnScreen())
            {
                MoveToPrimaryWorkArea();
            }
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            _logger.LogWarning("Floating window position load failed: {ErrorType}", exception.GetType().Name);
            MessageBox.Show("悬浮窗位置配置读取失败，已移回可见区域。", "周报",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool IsVisibleOnScreen()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var topLeft = PointToScreen(new System.Windows.Point(0, 0));
        var rectangle = new System.Drawing.Rectangle(
            (int)topLeft.X,
            (int)topLeft.Y,
            (int)(Width * dpi.DpiScaleX),
            (int)(Height * dpi.DpiScaleY));
        return Forms.Screen.AllScreens.Any(screen =>
        {
            var overlap = System.Drawing.Rectangle.Intersect(screen.WorkingArea, rectangle);
            return overlap.Width >= 64 * dpi.DpiScaleX && overlap.Height >= 32 * dpi.DpiScaleY;
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsLoaded && !IsVisibleOnScreen())
            {
                MoveToPrimaryWorkArea();
                SavePosition();
            }
        }));
    }

    private void MoveToPrimaryWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 20);
        Top = area.Top + 20;
    }

    private void UpdateWeek()
    {
        var week = WeekPeriod.FromDate(DateOnly.FromDateTime(DateTime.Today));
        // [旧代码-悬浮精灵] 卡片文本直写逻辑，卡片元素已迁往 FloatingCard，现按样式路由
        // WeekText.Text = $"本周：{week.Start:MM-dd} 至 {week.EndInclusive:MM-dd}";

        // [新增-悬浮精灵] 按样式路由本周文本（精灵侧用于悬停提示）
        var weekText = $"本周：{week.Start:MM-dd} 至 {week.EndInclusive:MM-dd}";
        if (_style == FloatingStyle.Sprite)
        {
            _sprite?.SetWeek(weekText);
        }
        else
        {
            _card?.SetWeek(weekText);
        }
    }

    // [旧代码-悬浮精灵] 原内联卡片事件处理器，随卡片迁往 FloatingCard 并由事件接线替代
    // private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    // {
    //     if (e.ButtonState == MouseButtonState.Pressed)
    //     {
    //         DragMove();
    //         SavePosition();
    //     }
    // }
    //
    // private void Open_Click(object sender, RoutedEventArgs e) => _openMain();
    //
    // private void Hide_Click(object sender, RoutedEventArgs e) => HideWithPosition();

    // [旧代码-悬浮精灵] 原位置记录类型，被 FloatingWindowPreferences（含样式字段）替代
    // private sealed record FloatingPosition(double Left, double Top);

    // [新增-悬浮精灵] 按样式构建内容并接线交互：拖动与原卡片标题栏行为一致（DragMove + 保存位置），
    // 打开/收起与原按钮行为一致；精灵另支持双击打开与右键菜单。
    private void BuildContent()
    {
        _sprite?.Dispose();
        _sprite = null;
        _card = null;
        FloatingContentHost.Children.Clear();

        if (_style == FloatingStyle.Sprite)
        {
            var sprite = new FloatingSprite(_machine);
            // [新增-机器人交互] 应用当前行为设置并转发气泡“打开周报”动作
            sprite.ApplyBehavior(_spriteBehavior);
            sprite.OpenWeekRequested += week => OpenWeekRequested?.Invoke(week);
            sprite.WorkRhythmReminderActionRequested += action =>
                WorkRhythmReminderActionRequested?.Invoke(action);
            sprite.WeeklyProgressRequested += userInitiated =>
                WeeklyProgressRequested?.Invoke(userInitiated);
            sprite.AdviceActionRequested += (action, week) =>
                AdviceActionRequested?.Invoke(action, week);
            sprite.AdviceShown += date =>
            {
                _spriteBehavior = _spriteBehavior with { LastAdviceDate = date };
                SavePosition();
            };
            // [新增-机器人交互] 持久化首次引导与每日问候日期
            sprite.GreetingShown += date =>
            {
                _spriteBehavior = _spriteBehavior with { LastGreetingDate = date };
                SavePosition();
            };
            sprite.GuideShown += () =>
            {
                _spriteBehavior = _spriteBehavior with { GuideShown = true };
                SavePosition();
            };
            sprite.DragPressed += () =>
            {
                DragMove();
                SavePosition();
            };
            sprite.OpenRequested += _openMain;
            sprite.HideRequested += HideWithPosition;
            _sprite = sprite;
            FloatingContentHost.Children.Add(sprite);
        }
        else
        {
            var card = new FloatingCard();
            card.SetStorageStatus(_weeklyProgressStatus);
            card.HeaderDragPressed += (_, _) =>
            {
                DragMove();
                SavePosition();
            };
            card.OpenRequested += (_, _) => _openMain();
            card.HideRequested += (_, _) => HideWithPosition();
            card.WeeklyProgressRequested += (_, _) => WeeklyProgressRequested?.Invoke(true);
            _card = card;
            FloatingContentHost.Children.Add(card);
        }

        ApplyWindowSize();
        UpdateWeek();
    }

    // [新增-悬浮精灵] 按样式调整窗口尺寸：卡片 320x165（XAML 默认），精灵 150x190（含跳动空间）
    private void ApplyWindowSize()
    {
        if (_style == FloatingStyle.Sprite)
        {
            Width = SpriteWidth;
            Height = SpriteHeight;
        }
        else
        {
            Width = 320;
            Height = 165;
        }
    }
}
