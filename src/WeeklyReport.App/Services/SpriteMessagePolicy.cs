// [新增-机器人交互] 精灵消息策略：夜间免打扰、每日一次问候、闲聊冷却与开关优先级。
// 纯函数，便于单元测试；时间读取 DateTimeOffset.DateTime（其携带的本地时钟），
// 不使用 LocalDateTime，避免测试结果依赖运行机器时区。
namespace WeeklyReport.App.Services;

public static class SpriteMessagePolicy
{
    private static readonly TimeOnly QuietStart = new(22, 0);
    private static readonly TimeOnly QuietEnd = new(8, 0);
    private static readonly TimeSpan InteractionCooldown = TimeSpan.FromMinutes(5);

    public static bool IsQuietHours(TimeOnly localTime) =>
        localTime >= QuietStart || localTime < QuietEnd;

    public static TimeSpan ChatterInterval(SpriteBehaviorPreset preset) => preset switch
    {
        SpriteBehaviorPreset.Quiet => TimeSpan.FromMinutes(120),
        SpriteBehaviorPreset.Lively => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromMinutes(90)
    };

    public static TimeSpan IdleAnimationInterval(SpriteBehaviorPreset preset) => preset switch
    {
        SpriteBehaviorPreset.Quiet => TimeSpan.FromSeconds(60),
        SpriteBehaviorPreset.Lively => TimeSpan.FromSeconds(20),
        _ => TimeSpan.FromSeconds(35)
    };

    public static SpriteMessage? CreateStartupGreeting(
        SpriteBehaviorSettings settings, DateTimeOffset now, DateOnly weekStart)
    {
        var local = now.DateTime;
        var today = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);
        if (!settings.Greetings
            || (settings.QuietHours && IsQuietHours(time))
            || settings.LastGreetingDate == today)
        {
            return null;
        }

        return CreateGreeting(now, weekStart);
    }

    public static SpriteMessage CreateGreeting(DateTimeOffset now, DateOnly weekStart) =>
        SpriteMessageCatalog.Greeting(now.DateTime, weekStart);

    public static SpriteMessage? CreateFirstRunGuide(
        SpriteBehaviorSettings settings, DateTimeOffset now)
    {
        var time = TimeOnly.FromDateTime(now.DateTime);
        if (settings.GuideShown
            || !settings.Greetings
            || (settings.QuietHours && IsQuietHours(time)))
        {
            return null;
        }

        return SpriteMessageCatalog.FirstRunGuide();
    }

    public static SpriteMessage? CreateIdleChatter(
        SpriteBehaviorSettings settings,
        DateTimeOffset now,
        TimeSpan sinceLastInteraction,
        TimeSpan sinceLastChatter,
        Random random)
    {
        var time = TimeOnly.FromDateTime(now.DateTime);
        if (!settings.Chatter
            || (settings.QuietHours && IsQuietHours(time))
            || sinceLastInteraction < InteractionCooldown
            || sinceLastChatter < ChatterInterval(settings.Preset))
        {
            return null;
        }

        return SpriteMessageCatalog.IdleChatter(random);
    }

    public static SpriteMessage? CreatePoke(
        SpriteBehaviorSettings settings, DateTimeOffset now, Random random)
    {
        var time = TimeOnly.FromDateTime(now.DateTime);
        if (!settings.Chatter || (settings.QuietHours && IsQuietHours(time)))
        {
            return null;
        }

        return SpriteMessageCatalog.Poke(random);
    }

    public static SpriteMessage CreateReminder(
        WeeklyReportSavedState state, DateOnly weekStart) =>
        SpriteMessageCatalog.Reminder(state, weekStart);

    public static SpriteMessage? CreateCelebration(
        SpriteBehaviorSettings settings, Random random) =>
        settings.Celebration ? SpriteMessageCatalog.Celebration(random) : null;
}
