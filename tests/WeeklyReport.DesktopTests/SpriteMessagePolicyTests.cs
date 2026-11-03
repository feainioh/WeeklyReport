// [新增-机器人交互] 精灵问候/闲聊/提醒策略测试：验证夜间边界、每日一次、档位频率、
// 开关优先级以及已保存/未保存提醒文案。所有时间使用显式偏移，不依赖运行机器时区。
using WeeklyReport.App.Services;

namespace WeeklyReport.DesktopTests;

public sealed class SpriteMessagePolicyTests
{
    private static readonly DateTimeOffset Morning =
        new(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(8));

    private static readonly DateOnly WeekStart = new(2026, 10, 26);

    [Fact]
    public void QuietHours_EndsAtEight()
    {
        Assert.True(SpriteMessagePolicy.IsQuietHours(new TimeOnly(22, 0)));
        Assert.True(SpriteMessagePolicy.IsQuietHours(new TimeOnly(7, 59)));
        Assert.False(SpriteMessagePolicy.IsQuietHours(new TimeOnly(8, 0)));
    }

    [Fact]
    public void StartupGreeting_IsSuppressedWhenAlreadyGreetedToday()
    {
        var settings = new SpriteBehaviorSettings
        {
            LastGreetingDate = DateOnly.FromDateTime(Morning.DateTime)
        };

        Assert.Null(SpriteMessagePolicy.CreateStartupGreeting(settings, Morning, WeekStart));
    }

    [Fact]
    public void StartupGreeting_IsSuppressedInQuietHours()
    {
        var lateNight = new DateTimeOffset(2026, 11, 2, 23, 0, 0, TimeSpan.FromHours(8));

        Assert.Null(SpriteMessagePolicy.CreateStartupGreeting(
            SpriteBehaviorSettings.Default, lateNight, WeekStart));
    }

    [Fact]
    public void StartupGreeting_ShowsInTheMorning()
    {
        var settings = new SpriteBehaviorSettings
        {
            LastGreetingDate = new DateOnly(2026, 11, 1)
        };

        var message = SpriteMessagePolicy.CreateStartupGreeting(
            settings, Morning, new DateOnly(2026, 11, 2));

        Assert.NotNull(message);
        Assert.Equal(SpriteMessageKind.StartupGreeting, message!.Kind);
        Assert.Contains("11-02", message.Text);
    }

    [Fact]
    public void PresetChatterIntervals_MatchConfirmedDefaults()
    {
        Assert.Equal(TimeSpan.FromMinutes(120),
            SpriteMessagePolicy.ChatterInterval(SpriteBehaviorPreset.Quiet));
        Assert.Equal(TimeSpan.FromMinutes(90),
            SpriteMessagePolicy.ChatterInterval(SpriteBehaviorPreset.Gentle));
        Assert.Equal(TimeSpan.FromMinutes(30),
            SpriteMessagePolicy.ChatterInterval(SpriteBehaviorPreset.Lively));
    }

    [Fact]
    public void IdleChatter_RequiresInteractionAndChatterCooldowns()
    {
        var settings = SpriteBehaviorSettings.Default;

        Assert.Null(SpriteMessagePolicy.CreateIdleChatter(
            settings, Morning, TimeSpan.FromMinutes(1), TimeSpan.Zero, new Random(0)));
        Assert.Null(SpriteMessagePolicy.CreateIdleChatter(
            settings, Morning, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(89), new Random(0)));

        var message = SpriteMessagePolicy.CreateIdleChatter(
            settings, Morning, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(91), new Random(0));

        Assert.NotNull(message);
        Assert.Equal(SpriteMessageKind.IdleChatter, message!.Kind);
    }

    [Fact]
    public void Poke_IsSuppressedWhenChatterDisabled()
    {
        var settings = new SpriteBehaviorSettings { Chatter = false };

        Assert.Null(SpriteMessagePolicy.CreatePoke(settings, Morning, new Random(0)));
    }

    [Fact]
    public void FirstRunGuide_IsShownOnce()
    {
        Assert.NotNull(SpriteMessagePolicy.CreateFirstRunGuide(SpriteBehaviorSettings.Default, Morning));
        Assert.Null(SpriteMessagePolicy.CreateFirstRunGuide(
            new SpriteBehaviorSettings { GuideShown = true }, Morning));
    }

    [Fact]
    public void Reminder_UnsavedCarriesOpenAction()
    {
        var message = SpriteMessagePolicy.CreateReminder(WeeklyReportSavedState.NotSaved, WeekStart);

        Assert.Contains("还没保存", message.Text);
        Assert.Equal("打开周报", message.ActionLabel);
    }

    [Fact]
    public void Reminder_SavedIsSoftAndHasNoAction()
    {
        var message = SpriteMessagePolicy.CreateReminder(WeeklyReportSavedState.Saved, WeekStart);

        Assert.DoesNotContain("还没保存", message.Text);
        Assert.Null(message.ActionLabel);
    }

    [Fact]
    public void Celebration_IsSuppressedWhenDisabled()
    {
        var settings = new SpriteBehaviorSettings { Celebration = false };

        Assert.Null(SpriteMessagePolicy.CreateCelebration(settings, new Random(0)));
    }
}
