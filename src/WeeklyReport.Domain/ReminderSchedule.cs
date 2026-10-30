namespace WeeklyReport.Domain;

public enum ReminderKind
{
    ThursdayAfternoon,
    FridayMorning
}

[Flags]
public enum ReminderTimeAdjustment
{
    None = 0,
    ShiftedInvalidLocalTime = 1,
    EarlierAmbiguousOccurrence = 2
}

public readonly record struct ReminderKey
{
    public ReminderKey(DateOnly weekStart, ReminderKind kind)
    {
        _ = new WeekPeriod(weekStart);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        WeekStart = weekStart;
        Kind = kind;
    }

    public DateOnly WeekStart { get; }
    public ReminderKind Kind { get; }
}

public sealed record ScheduledReminder(
    ReminderKey Key,
    DateTimeOffset DueAt,
    ReminderTimeAdjustment Adjustment);

public sealed class ReminderSchedule
{
    private static readonly TimeSpan CatchUpWindow = TimeSpan.FromHours(2);

    public IReadOnlyList<ScheduledReminder> ForWeek(WeekPeriod week, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return
        [
            Create(week, ReminderKind.ThursdayAfternoon, 3, new TimeOnly(17, 0), timeZone),
            Create(week, ReminderKind.FridayMorning, 4, new TimeOnly(10, 0), timeZone)
        ];
    }

    public IReadOnlyList<ScheduledReminder> GetDue(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        IReadOnlySet<ReminderKey> delivered)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(delivered);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var week = WeekPeriod.FromDate(localDate);
        return ForWeek(week, timeZone)
            .Where(item => !delivered.Contains(item.Key)
                && item.DueAt <= now
                && now - item.DueAt <= CatchUpWindow)
            .OrderBy(item => item.DueAt)
            .ToArray();
    }

    public ScheduledReminder GetNext(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        IReadOnlySet<ReminderKey> delivered)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(delivered);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        var week = WeekPeriod.FromDate(localDate);

        for (var index = 0; index <= delivered.Count + 1; index++)
        {
            var next = ForWeek(week, timeZone)
                .Where(item => item.DueAt > now && !delivered.Contains(item.Key))
                .MinBy(item => item.DueAt);
            if (next is not null)
            {
                return next;
            }

            week = new WeekPeriod(week.Start.AddDays(7));
        }

        throw new InvalidOperationException("无法计算下一次周报提醒。");
    }

    private static ScheduledReminder Create(
        WeekPeriod week,
        ReminderKind kind,
        int dayOffset,
        TimeOnly time,
        TimeZoneInfo timeZone)
    {
        var local = week.Start.AddDays(dayOffset).ToDateTime(time, DateTimeKind.Unspecified);
        var adjustment = ReminderTimeAdjustment.None;
        if (timeZone.IsInvalidTime(local))
        {
            local = FirstValidLocalTime(local, timeZone);
            adjustment |= ReminderTimeAdjustment.ShiftedInvalidLocalTime;
        }

        TimeSpan offset;
        if (timeZone.IsAmbiguousTime(local))
        {
            offset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            adjustment |= ReminderTimeAdjustment.EarlierAmbiguousOccurrence;
        }
        else
        {
            offset = timeZone.GetUtcOffset(local);
        }

        return new ScheduledReminder(new ReminderKey(week.Start, kind), new DateTimeOffset(local, offset), adjustment);
    }

    private static DateTime FirstValidLocalTime(DateTime invalid, TimeZoneInfo timeZone)
    {
        var upper = invalid;
        for (var minute = 0; minute < 24 * 60 && timeZone.IsInvalidTime(upper); minute++)
        {
            upper = upper.AddMinutes(1);
        }

        if (timeZone.IsInvalidTime(upper))
        {
            throw new InvalidOperationException("无法解析当地提醒时间，请检查系统时区。");
        }

        var lowerTicks = invalid.Ticks;
        var upperTicks = upper.Ticks;
        while (upperTicks - lowerTicks > 1)
        {
            var midpoint = lowerTicks + (upperTicks - lowerTicks) / 2;
            if (timeZone.IsInvalidTime(new DateTime(midpoint, DateTimeKind.Unspecified)))
            {
                lowerTicks = midpoint;
            }
            else
            {
                upperTicks = midpoint;
            }
        }

        return new DateTime(upperTicks, DateTimeKind.Unspecified);
    }
}
