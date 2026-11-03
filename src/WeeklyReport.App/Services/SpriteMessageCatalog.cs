// [新增-机器人交互] 悬浮精灵内置文案池：问候、首次引导、闲聊、戳一戳、提醒与庆祝。
// 全部为本地静态中文文案，不接 AI、不发网络请求；随机池避免连续重复由调用方控制。
using WeeklyReport.Domain;

namespace WeeklyReport.App.Services;

public enum SpriteMessageKind
{
    StartupGreeting,
    FirstRunGuide,
    IdleChatter,
    Poke,
    ReminderUnsaved,
    ReminderSaved,
    ReminderUnknown,
    Celebration
}

public sealed record SpriteMessage(
    SpriteMessageKind Kind,
    string Text,
    string? ActionLabel = null);

public static class SpriteMessageCatalog
{
    private static readonly string[] ChatterMessages =
    [
        "记得喝口水，起来活动一下吧。",
        "工作再忙，也别忘了记录今天做了什么。",
        "本周的周报进展还顺利吗？",
        "眼睛累了就看看远处吧。",
        "我在这里陪你，慢慢来。",
        "今天的任务完成得怎么样？"
    ];

    private static readonly string[] PokeMessages =
    [
        "别戳啦，我在呢。",
        "需要我打开周报吗？",
        "再戳我就要跳舞了。",
        "我正在待命，随时可以出发。",
        "双击我可以直接打开周报。"
    ];

    private static readonly string[] CelebrationMessages =
    [
        "周报保存好了，辛苦啦。",
        "保存成功，今天也很棒。",
        "周报已保存，可以安心休息一下了。"
    ];

    public static SpriteMessage Greeting(DateTime localNow, DateOnly weekStart)
    {
        var week = new WeekPeriod(weekStart);
        var range = $"{week.Start:MM-dd} 至 {week.EndInclusive:MM-dd}";
        var prefix = localNow.Hour switch
        {
            >= 5 and < 11 => "早上好",
            >= 11 and < 14 => "中午好",
            >= 14 and < 18 => "下午好",
            _ => "晚上好"
        };
        return new SpriteMessage(
            SpriteMessageKind.StartupGreeting,
            $"{prefix}，本周 {range}。");
    }

    public static SpriteMessage FirstRunGuide() =>
        new(SpriteMessageKind.FirstRunGuide,
            "拖住我可以换位置；双击打开周报；右键可以收起我。");

    public static SpriteMessage IdleChatter(Random random) =>
        new(SpriteMessageKind.IdleChatter, Pick(ChatterMessages, random));

    public static SpriteMessage Poke(Random random) =>
        new(SpriteMessageKind.Poke, Pick(PokeMessages, random));

    public static SpriteMessage Reminder(WeeklyReportSavedState state, DateOnly weekStart)
    {
        var week = new WeekPeriod(weekStart);
        var range = $"{week.Start:MM-dd} 至 {week.EndInclusive:MM-dd}";
        return state switch
        {
            WeeklyReportSavedState.NotSaved => new SpriteMessage(
                SpriteMessageKind.ReminderUnsaved,
                $"本周（{range}）周报还没保存，要现在打开吗？",
                "打开周报"),
            WeeklyReportSavedState.Saved => new SpriteMessage(
                SpriteMessageKind.ReminderSaved,
                "本周周报已保存，提醒你确认一下有没有要补充的。"),
            _ => new SpriteMessage(
                SpriteMessageKind.ReminderUnknown,
                "本周周报提醒：暂时无法读取保存状态，打开周报确认一下吧。",
                "打开周报")
        };
    }

    public static SpriteMessage Celebration(Random random) =>
        new(SpriteMessageKind.Celebration, Pick(CelebrationMessages, random));

    private static string Pick(IReadOnlyList<string> values, Random random) =>
        values[random.Next(values.Count)];
}
