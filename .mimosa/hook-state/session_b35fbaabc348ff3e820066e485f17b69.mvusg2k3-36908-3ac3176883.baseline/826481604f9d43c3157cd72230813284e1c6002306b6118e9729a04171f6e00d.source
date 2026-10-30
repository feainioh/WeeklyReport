using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WeeklyReport.App.Services;
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

    public FloatingWindow(string positionPath, Action openMain, ILogger<FloatingWindow> logger)
    {
        InitializeComponent();
        _positionPath = positionPath;
        _openMain = openMain;
        _logger = logger;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => UpdateWeek();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _timer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void HideWithPosition()
    {
        SavePosition();
        Hide();
    }

    public void SetReminderStatus(string message)
    {
        ReminderStatusText.Text = message;
        ReminderStatusText.ToolTip = message;
    }

    public void SavePosition()
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_positionPath))
                ?? throw new InvalidOperationException("无法确定悬浮窗配置目录。");
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".floating-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(new FloatingPosition(Left, Top)));
                File.Move(temporary, _positionPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
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
            if (!File.Exists(_positionPath))
            {
                return;
            }

            var position = JsonSerializer.Deserialize<FloatingPosition>(File.ReadAllText(_positionPath))
                ?? throw new InvalidDataException("悬浮窗位置配置为空。");
            if (!double.IsFinite(position.Left) || !double.IsFinite(position.Top)
                || Math.Abs(position.Left) > 100000 || Math.Abs(position.Top) > 100000)
            {
                throw new InvalidDataException("悬浮窗位置配置无效。");
            }

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
        WeekText.Text = $"本周：{week.Start:MM-dd} 至 {week.EndInclusive:MM-dd}";
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            SavePosition();
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e) => _openMain();

    private void Hide_Click(object sender, RoutedEventArgs e) => HideWithPosition();

    private sealed record FloatingPosition(double Left, double Top);
}
