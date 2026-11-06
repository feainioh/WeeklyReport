namespace WeeklyReport.App.Services;

public enum SpriteActionPriority
{
    State = 0,
    User = 1,
    Celebration = 2,
    Automatic = 3,
    Ambient = 4
}

public static class SpriteActionPriorityRules
{
    public static bool CanStart(SpriteActionPriority? active, SpriteActionPriority next) =>
        active is null || next < active.Value;
}
