using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace WeeklyReport.App.Services;

public sealed class ThemeService(ResourceDictionary resources, Dispatcher dispatcher) : IDisposable
{
    public void Start()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        dispatcher.BeginInvoke(new Action(Apply));

    private void Apply()
    {
        if (SystemParameters.HighContrast)
        {
            resources["WindowBrush"] = SystemColors.WindowBrush;
            resources["SurfaceBrush"] = SystemColors.WindowBrush;
            resources["TextBrush"] = SystemColors.WindowTextBrush;
            resources["MutedBrush"] = SystemColors.WindowTextBrush;
            resources["AccentBrush"] = SystemColors.HighlightBrush;
            resources["AccentTextBrush"] = SystemColors.HighlightTextBrush;

            // [新增-悬浮精灵] 高对比度下精灵身体使用系统窗口色，保证与文字对比可读
            resources["SpriteBodyBrush"] = SystemColors.WindowBrush;

            // [新增-样式现代化] 高对比度悬停底色
            resources["HoverBrush"] = SystemColors.HighlightBrush;
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var light = key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        resources["WindowBrush"] = light ? Brush(0xF5, 0xF6, 0xF8) : Brush(0x20, 0x20, 0x20);
        resources["SurfaceBrush"] = light ? Brush(0xFF, 0xFF, 0xFF) : Brush(0x2C, 0x2C, 0x2C);
        resources["TextBrush"] = light ? Brush(0x1B, 0x1B, 0x1B) : Brush(0xF4, 0xF4, 0xF4);
        resources["MutedBrush"] = light ? Brush(0x55, 0x55, 0x55) : Brush(0xC5, 0xC5, 0xC5);

        // [新增-悬浮精灵] 精灵身体专用色：浅色近白、深色微降亮度，避免深色主题下纯白刺眼
        resources["SpriteBodyBrush"] = light ? Brush(0xF6, 0xF8, 0xFB) : Brush(0xE8, 0xEC, 0xF2);

        // [新增-样式现代化] 悬停底色：浅色 8% 黑、深色 8% 白的半透明叠加，任何表面色上都自然
        var hoverChannel = (byte)(light ? 0x00 : 0xFF);
        var hover = Color.FromArgb(0x14, hoverChannel, hoverChannel, hoverChannel);
        resources["HoverBrush"] = new SolidColorBrush(hover);
        var accent = SystemParameters.WindowGlassColor;
        accent.A = byte.MaxValue;
        resources["AccentBrush"] = new SolidColorBrush(accent);
        var luminance = (0.2126 * Linear(accent.R) + 0.7152 * Linear(accent.G) + 0.0722 * Linear(accent.B));
        resources["AccentTextBrush"] = luminance > 0.179 ? Brushes.Black : Brushes.White;
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Color.FromRgb(red, green, blue));

    private static double Linear(byte value)
    {
        var component = value / 255.0;
        return component <= 0.04045 ? component / 12.92 : Math.Pow((component + 0.055) / 1.055, 2.4);
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
