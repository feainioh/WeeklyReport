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

    // [新增-机器人交互] 旧文件缺少 Sprite 设置时回退默认行为
    [Fact]
    public void LegacyFileWithoutSpriteSettings_LoadsDefaultBehavior()
    {
        File.WriteAllText(_path, """{"Left":100,"Top":200,"Style":1}""");

        var loaded = FloatingWindowPreferencesStore.TryLoad(_path);

        Assert.NotNull(loaded);
        Assert.Equal(SpriteBehaviorSettings.Default, loaded!.Sprite);
    }

    // [新增-机器人交互] 精灵行为设置完整往返
    [Fact]
    public void SaveThenLoad_RoundTripsSpriteBehavior()
    {
        var behavior = new SpriteBehaviorSettings
        {
            Preset = SpriteBehaviorPreset.Lively,
            Greetings = false,
            Chatter = true,
            HoverReaction = false,
            ReminderBubble = true,
            Celebration = false,
            QuietHours = false,
            GuideShown = true,
            LastGreetingDate = new DateOnly(2026, 11, 2),
            DataAdvice = false,
            LastAdviceDate = new DateOnly(2026, 11, 1),
            DynamicActions = false,
            AutoIdleActions = false
        };

        FloatingWindowPreferencesStore.Save(_path,
            new FloatingWindowPreferences(12.5, -30, FloatingStyle.Sprite, behavior));

        var loaded = FloatingWindowPreferencesStore.TryLoad(_path);

        Assert.NotNull(loaded);
        Assert.Equal(behavior, loaded!.Sprite);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsWorkRhythmSettings()
    {
        var settings = new WorkRhythmReminderSettings
        {
            Enabled = true,
            SnoozedUntil = new DateTimeOffset(2026, 11, 2, 10, 10, 0, TimeSpan.FromHours(8)),
            LastReminderAt = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(8))
        };
        FloatingWindowPreferencesStore.Save(_path,
            new FloatingWindowPreferences(12.5, -30, FloatingStyle.Sprite, WorkRhythm: settings));

        var loaded = FloatingWindowPreferencesStore.TryLoad(_path);

        Assert.NotNull(loaded);
        Assert.Equal(settings, loaded!.WorkRhythm);
    }

    [Fact]
    public void LegacyFileWithoutWorkRhythmSettings_DisablesNewReminder()
    {
        File.WriteAllText(_path, """{"Left":100,"Top":200,"Style":1}""");

        var loaded = FloatingWindowPreferencesStore.TryLoad(_path);

        Assert.NotNull(loaded);
        Assert.Equal(WorkRhythmReminderSettings.Default, loaded!.WorkRhythm);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void LegacySpriteSettingsWithoutAdviceUsePresetDefault(int preset, bool expected)
    {
        File.WriteAllText(_path,
            "{\"Left\":1,\"Top\":2,\"Style\":1,\"Sprite\":{\"Preset\":" + preset + "}}");

        var loaded = FloatingWindowPreferencesStore.TryLoad(_path);

        Assert.NotNull(loaded);
        Assert.Equal(expected, loaded!.Sprite!.DataAdvice);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void LegacySpriteSettingsWithoutAnimationOptionsUsePresetDefaults(
        int preset, bool expectedAutoIdleActions)
    {
        File.WriteAllText(_path,
            "{\"Left\":1,\"Top\":2,\"Style\":1,\"Sprite\":{\"Preset\":" + preset + "}}");

        var loaded = FloatingWindowPreferencesStore.TryLoad(_path);

        Assert.NotNull(loaded);
        Assert.True(loaded!.Sprite!.DynamicActions);
        Assert.Equal(expectedAutoIdleActions, loaded.Sprite.AutoIdleActions);
    }

    // [新增-机器人交互] 未知档位值回退默认行为，不丢失位置/样式
    [Fact]
    public void UnknownSpritePreset_FallsBackToDefaultBehavior()
    {
        File.WriteAllText(_path, """{"Left":1,"Top":2,"Style":1,"Sprite":{"Preset":99}}""");

        var loaded = FloatingWindowPreferencesStore.TryLoad(_path);

        Assert.NotNull(loaded);
        Assert.Equal(SpriteBehaviorSettings.Default, loaded!.Sprite);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
