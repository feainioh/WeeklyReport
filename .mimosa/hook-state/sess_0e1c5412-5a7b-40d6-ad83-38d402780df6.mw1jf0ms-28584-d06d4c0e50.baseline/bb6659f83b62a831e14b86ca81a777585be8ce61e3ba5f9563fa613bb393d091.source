// [新增-机器人交互] 悬浮精灵行为配置：性格档位、交互/提醒/动画开关，以及跨重启保存的引导与问候状态。
namespace WeeklyReport.App.Services;

public enum SpriteBehaviorPreset
{
    Quiet = 0,
    Gentle = 1,
    Lively = 2
}

public sealed record SpriteBehaviorSettings
{
    public SpriteBehaviorPreset Preset { get; init; } = SpriteBehaviorPreset.Gentle;
    public bool Greetings { get; init; } = true;
    public bool Chatter { get; init; } = true;
    public bool HoverReaction { get; init; } = true;
    public bool ReminderBubble { get; init; } = true;
    public bool Celebration { get; init; } = true;
    public bool QuietHours { get; init; } = true;
    public bool DataAdvice { get; init; } = true;
    public bool DynamicActions { get; init; } = true;
    public bool AutoIdleActions { get; init; } = true;
    public bool GuideShown { get; init; }
    public DateOnly? LastGreetingDate { get; init; }
    public DateOnly? LastAdviceDate { get; init; }

    public static SpriteBehaviorSettings Default { get; } = new();

    // [新增-机器人交互] 切换档位时恢复该档位默认开关，但保留引导/问候历史
    public static SpriteBehaviorSettings ForPreset(
        SpriteBehaviorPreset preset, SpriteBehaviorSettings current) =>
        (preset switch
        {
            SpriteBehaviorPreset.Quiet => new SpriteBehaviorSettings
            {
                Preset = preset,
                Greetings = false,
                Chatter = false,
                HoverReaction = true,
                ReminderBubble = true,
                Celebration = true,
                QuietHours = true,
                DataAdvice = false,
                DynamicActions = true,
                AutoIdleActions = false
            },
            SpriteBehaviorPreset.Lively => new SpriteBehaviorSettings
            {
                Preset = preset,
                DynamicActions = true,
                AutoIdleActions = true
            },
            _ => new SpriteBehaviorSettings { Preset = SpriteBehaviorPreset.Gentle }
        }) with
        {
            GuideShown = current.GuideShown,
            LastGreetingDate = current.LastGreetingDate,
            LastAdviceDate = current.LastAdviceDate
        };
}
