namespace WeeklyReport.App.Services;

public enum WorkRhythmReminderAction
{
    Acknowledge,
    Snooze
}

public sealed record WorkRhythmReminderSettings
{
    public bool Enabled { get; init; }
    public DateTimeOffset? SnoozedUntil { get; init; }
    public DateTimeOffset? LastReminderAt { get; init; }

    public static WorkRhythmReminderSettings Default { get; } = new();
}

public static class WorkRhythmReminderPolicy
{
    private static readonly TimeOnly[] ReminderTimes =
    [
        new(10, 0),
        new(11, 0),
        new(14, 0),
        new(15, 0),
        new(16, 0),
        new(17, 0)
    ];

    private static readonly TimeSpan MaximumCheckGap = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MergeWindow = TimeSpan.FromMinutes(5);

    public static DateTimeOffset? GetDueTime(
        DateTimeOffset previousCheck,
        DateTimeOffset now,
        WorkRhythmReminderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled || now < previousCheck || now - previousCheck > MaximumCheckGap)
        {
            return null;
        }

        if (settings.SnoozedUntil is { } snoozedUntil
            && snoozedUntil > previousCheck
            && snoozedUntil <= now
            && IsWorkingTime(snoozedUntil)
            && !HasNearbyScheduledReminder(snoozedUntil)
            && settings.LastReminderAt != snoozedUntil)
        {
            return snoozedUntil;
        }

        var localNow = now.DateTime;
        if (!IsWorkingDay(localNow.DayOfWeek))
        {
            return null;
        }

        foreach (var time in ReminderTimes)
        {
            var localScheduledTime = localNow.Date.Add(time.ToTimeSpan());
            var scheduledTime = new DateTimeOffset(localScheduledTime, now.Offset);
            if (scheduledTime > previousCheck
                && scheduledTime <= now
                && (settings.SnoozedUntil is null || settings.SnoozedUntil <= previousCheck)
                && settings.LastReminderAt != scheduledTime)
            {
                return scheduledTime;
            }
        }

        return null;
    }

    public static bool IsWorkingTime(DateTimeOffset value)
    {
        var local = value.DateTime;
        if (!IsWorkingDay(local.DayOfWeek))
        {
            return false;
        }

        var time = TimeOnly.FromDateTime(local);
        return time >= new TimeOnly(9, 0) && time < new TimeOnly(12, 0)
            || time >= new TimeOnly(13, 0) && time < new TimeOnly(18, 0);
    }

    private static bool IsWorkingDay(DayOfWeek day) =>
        day is >= DayOfWeek.Monday and <= DayOfWeek.Friday;

    private static bool HasNearbyScheduledReminder(DateTimeOffset snoozedUntil)
    {
        var localSnooze = snoozedUntil.DateTime;
        var next = ReminderTimes
            .Select(time => localSnooze.Date.Add(time.ToTimeSpan()))
            .Where(time => time > localSnooze)
            .Select(time => new DateTimeOffset(time, snoozedUntil.Offset))
            .FirstOrDefault();

        return next != default && next - snoozedUntil <= MergeWindow;
    }
}

public static class WorkRhythmReminderActivation
{
    public static bool TryParse(string? arguments, out WorkRhythmReminderAction action)
    {
        action = default;
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        var separator = arguments.Contains(';') ? ';' : '&';
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in arguments.Split(separator))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0 || !values.TryAdd(part[..equals], part[(equals + 1)..]))
            {
                return false;
            }
        }

        if (values.Count != 2
            || !values.TryGetValue("action", out var actionName)
            || actionName != "work-rhythm"
            || !values.TryGetValue("intent", out var intent))
        {
            return false;
        }

        action = intent switch
        {
            "acknowledge" => WorkRhythmReminderAction.Acknowledge,
            "snooze" => WorkRhythmReminderAction.Snooze,
            _ => default
        };
        return intent is "acknowledge" or "snooze";
    }
}
