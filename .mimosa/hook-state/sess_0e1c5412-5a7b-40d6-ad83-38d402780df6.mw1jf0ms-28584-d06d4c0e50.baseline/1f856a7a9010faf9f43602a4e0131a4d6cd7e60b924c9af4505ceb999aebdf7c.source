// [新增-形象动画2] 精灵调色板对比度与强调色文字可读性测试。
using System.Windows.Media;
using WeeklyReport.App.Services;

namespace WeeklyReport.DesktopTests;

public sealed class SpritePaletteTests
{
    [Fact]
    public void LightPalette_MeetsContrastTargets()
    {
        var palette = SpritePalette.For(SpriteTheme.Light);

        Assert.True(SpritePalette.ContrastRatio(palette.Body, palette.Ink) >= 10.0);
        Assert.True(SpritePalette.ContrastRatio(palette.Body, palette.Detail) >= 4.5);
    }

    [Fact]
    public void DarkPalette_MeetsContrastTargets()
    {
        var palette = SpritePalette.For(SpriteTheme.Dark);

        Assert.True(SpritePalette.ContrastRatio(palette.Body, palette.Ink) >= 3.0);
        Assert.True(SpritePalette.ContrastRatio(palette.Body, palette.Detail) >= 3.0);
    }

    [Theory]
    [InlineData(0xFF, 0xD5, 0x4F)]
    [InlineData(0x5B, 0x8D, 0xEF)]
    [InlineData(0x11, 0x18, 0x27)]
    [InlineData(0xF4, 0x72, 0xB6)]
    public void AccentText_IsReadableOnAccent(byte red, byte green, byte blue)
    {
        var accent = Color.FromRgb(red, green, blue);
        var text = SpritePalette.AccentTextFor(accent);

        Assert.True(SpritePalette.ContrastRatio(accent, text) >= 4.5);
    }
}
