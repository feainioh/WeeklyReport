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
    // [新增-界面现代化2] 主题应用完成通知：MainWindow 借此刷新 Win11 标题栏着色
    public event Action? ThemeChanged;

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

            resources["SpriteBodyShadeBrush"] = SystemColors.WindowBrush;
            resources["SpriteInkBrush"] = SystemColors.WindowTextBrush;
            resources["SpriteDetailBrush"] = SystemColors.WindowTextBrush;
            resources["SpriteEyeBrush"] = SystemColors.HighlightTextBrush;
            resources["SpriteRimBrush"] = Brushes.Transparent;
            resources["SpriteHighlightBrush"] = Brushes.Transparent;
            resources["SpriteShadeBrush"] = Brushes.Transparent;
            resources["SpriteShadowBrush"] = Brushes.Transparent;
            resources["AntennaAlertBrush"] = SystemColors.WindowTextBrush;
            resources["AntennaSuccessBrush"] = SystemColors.WindowTextBrush;

            // [旧代码-形象动画2] 原高对比度仅替换机身色。
            // resources["SpriteBodyBrush"] = SystemColors.WindowBrush;
            // [新增-形象动画2] 高对比度细节统一走系统颜色。
            resources["SpriteBodyBrush"] = SystemColors.WindowBrush;

            // [新增-样式现代化] 高对比度悬停底色
            resources["HoverBrush"] = SystemColors.HighlightBrush;

            // [新增-界面现代化2] 高对比度：卡片边框、列表选中与分隔线改用系统颜色保证可读
            resources["CardBorderBrush"] = SystemColors.WindowTextBrush;
            resources["ListSelectionBrush"] = SystemColors.HighlightBrush;
            resources["ListSelectionTextBrush"] = SystemColors.HighlightTextBrush;
            resources["RowSeparatorBrush"] = SystemColors.WindowTextBrush;
            ThemeChanged?.Invoke();
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var light = key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        resources["WindowBrush"] = light ? Brush(0xF5, 0xF6, 0xF8) : Brush(0x20, 0x20, 0x20);
        resources["SurfaceBrush"] = light ? Brush(0xFF, 0xFF, 0xFF) : Brush(0x2C, 0x2C, 0x2C);
        resources["TextBrush"] = light ? Brush(0x1B, 0x1B, 0x1B) : Brush(0xF4, 0xF4, 0xF4);
        resources["MutedBrush"] = light ? Brush(0x55, 0x55, 0x55) : Brush(0xC5, 0xC5, 0xC5);

        // [旧代码-形象动画2] 原浅色/深色精灵机身配色。
        // resources["SpriteBodyBrush"] = light ? Brush(0xF6, 0xF8, 0xFB) : Brush(0xE8, 0xEC, 0xF2);

        // [新增-样式现代化] 悬停底色：浅色 8% 黑、深色 8% 白的半透明叠加，任何表面色上都自然
        var hoverChannel = (byte)(light ? 0x00 : 0xFF);
        var hover = Color.FromArgb(0x14, hoverChannel, hoverChannel, hoverChannel);
        resources["HoverBrush"] = new SolidColorBrush(hover);
        var accent = SystemParameters.WindowGlassColor;
        accent.A = byte.MaxValue;
        resources["AccentBrush"] = new SolidColorBrush(accent);
        resources["AccentTextBrush"] = new SolidColorBrush(SpritePalette.AccentTextFor(accent));
        var palette = SpritePalette.For(light ? SpriteTheme.Light : SpriteTheme.Dark);
        resources["SpriteBodyBrush"] = Brush(palette.Body);
        resources["SpriteBodyShadeBrush"] = Brush(palette.BodyShade);
        resources["SpriteInkBrush"] = Brush(palette.Ink);
        resources["SpriteDetailBrush"] = Brush(palette.Detail);
        resources["SpriteRimBrush"] = Brush(palette.Rim);
        resources["SpriteHighlightBrush"] = Brush(palette.Highlight);
        resources["SpriteShadeBrush"] = Brush(palette.Shade);
        resources["SpriteShadowBrush"] = Brush(palette.Shadow);
        resources["SpriteEyeBrush"] = new SolidColorBrush(SpritePalette.AccentTextFor(accent));
        resources["AntennaAlertBrush"] = Brush(palette.Alert);
        resources["AntennaSuccessBrush"] = Brush(palette.Success);

        // [新增-界面现代化2] 卡片弱边框：浅色 8% 黑、深色 12% 白，替代 1px 实色
        resources["CardBorderBrush"] = new SolidColorBrush(light
            ? Color.FromArgb(0x14, 0x00, 0x00, 0x00)
            : Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

        // [新增-界面现代化2] 列表选中底色：10% 强调色；文字在正常主题下沿用正文色
        resources["ListSelectionBrush"] = new SolidColorBrush(Color.FromArgb(0x1A, accent.R, accent.G, accent.B));
        resources["ListSelectionTextBrush"] = resources["TextBrush"];

        // [新增-界面现代化2] 列表行分隔线：Muted 色降到 40% 透明度，减少视觉噪音
        var muted = ((SolidColorBrush)resources["MutedBrush"]).Color;
        resources["RowSeparatorBrush"] = new SolidColorBrush(Color.FromArgb(0x66, muted.R, muted.G, muted.B));

        ThemeChanged?.Invoke();
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Color.FromRgb(red, green, blue));

    private static SolidColorBrush Brush(Color color) => new(color);

    private static double Linear(byte value)
    {
        var component = value / 255.0;
        return component <= 0.04045 ? component / 12.92 : Math.Pow((component + 0.055) / 1.055, 2.4);
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
