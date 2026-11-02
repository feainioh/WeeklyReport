// [新增-悬浮精灵] 悬浮窗偏好读写测试：新格式往返、旧文件缺 Style 字段兼容、无效内容与无效样式拒绝。
using System.IO;
using WeeklyReport.App.Services;

namespace WeeklyReport.DesktopTests;

public sealed class FloatingPreferencesTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"wr-prefs-{Guid.NewGuid():N}.json");

    [Fact]
    public void MissingFile_ReturnsNull()
    {
        Assert.Null(FloatingWindowPreferencesStore.TryLoad(_path));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsPositionAndStyle()
    {
        FloatingWindowPreferencesStore.Save(_path, new FloatingWindowPreferences(12.5, -30, FloatingStyle.Sprite));

        var loaded = FloatingWindowPreferencesStore.TryLoad(_path);

        Assert.NotNull(loaded);
        Assert.Equal(12.5, loaded!.Left);
        Assert.Equal(-30, loaded.Top);
        Assert.Equal(FloatingStyle.Sprite, loaded.Style);
    }

    [Fact]
    public void LegacyFileWithoutStyle_LoadsAsCard()
    {
        File.WriteAllText(_path, """{"Left":100,"Top":200}""");

        var loaded = FloatingWindowPreferencesStore.TryLoad(_path);

        Assert.NotNull(loaded);
        Assert.Equal(100, loaded!.Left);
        Assert.Equal(200, loaded.Top);
        Assert.Equal(FloatingStyle.Card, loaded.Style);
    }

    [Fact]
    public void InvalidJson_ReturnsNull()
    {
        File.WriteAllText(_path, "not-json");

        Assert.Null(FloatingWindowPreferencesStore.TryLoad(_path));
    }

    [Fact]
    public void OutOfRangePosition_ReturnsNull()
    {
        File.WriteAllText(_path, """{"Left":1e9,"Top":0}""");

        Assert.Null(FloatingWindowPreferencesStore.TryLoad(_path));
    }

    [Fact]
    public void UnknownStyleValue_ReturnsNull()
    {
        File.WriteAllText(_path, """{"Left":1,"Top":2,"Style":7}""");

        Assert.Null(FloatingWindowPreferencesStore.TryLoad(_path));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
