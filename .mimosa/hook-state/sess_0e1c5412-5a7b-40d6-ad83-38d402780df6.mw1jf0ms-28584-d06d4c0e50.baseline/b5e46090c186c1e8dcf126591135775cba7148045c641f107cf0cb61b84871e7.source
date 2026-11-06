using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace WeeklyReport.App.Services;

public enum SpriteTheme
{
    Light,
    Dark
}

public sealed record SpritePaletteColors(
    Color Body,
    Color BodyShade,
    Color Ink,
    Color Detail,
    Color Rim,
    Color Highlight,
    Color Shade,
    Color Shadow,
    Color Alert,
    Color Success);

public static class SpritePalette
{
    public static SpritePaletteColors For(SpriteTheme theme) => theme switch
    {
        SpriteTheme.Light => new SpritePaletteColors(
            Color.FromRgb(0xF1, 0xF5, 0xFB),
            Color.FromRgb(0xDD, 0xE6, 0xF2),
            Color.FromRgb(0x2C, 0x36, 0x46),
            Color.FromRgb(0x5A, 0x69, 0x7E),
            Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x14, 0x00, 0x00, 0x00),
            Color.FromArgb(0x29, 0x00, 0x00, 0x00),
            Color.FromRgb(0xC2, 0x41, 0x0C),
            Color.FromRgb(0x15, 0x80, 0x3D)),
        SpriteTheme.Dark => new SpritePaletteColors(
            Color.FromRgb(0x6E, 0x7A, 0x8A),
            Color.FromRgb(0x5C, 0x67, 0x77),
            Color.FromRgb(0xED, 0xF2, 0xF9),
            Color.FromRgb(0xD9, 0xE2, 0xEF),
            Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x38, 0x00, 0x00, 0x00),
            Color.FromArgb(0x29, 0x00, 0x00, 0x00),
            Color.FromRgb(0xFF, 0x9A, 0x4D),
            Color.FromRgb(0x5B, 0xD6, 0x8A)),
        _ => throw new ArgumentOutOfRangeException(nameof(theme))
    };

    public static Color AccentTextFor(Color accent) =>
        RelativeLuminance(accent) > 0.179
            ? Color.FromRgb(0, 0, 0)
            : Color.FromRgb(255, 255, 255);

    public static double ContrastRatio(Color first, Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color) =>
        0.2126 * Linear(color.R)
        + 0.7152 * Linear(color.G)
        + 0.0722 * Linear(color.B);

    private static double Linear(byte value)
    {
        var component = value / 255.0;
        return component <= 0.04045
            ? component / 12.92
            : Math.Pow((component + 0.055) / 1.055, 2.4);
    }
}
