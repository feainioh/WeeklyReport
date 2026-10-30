using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public class ReminderScheduleTests
{
    private static readonly TimeZoneInfo UtcPlusEight =
        TimeZoneInfo.CreateCustomTimeZone("UTC+08", TimeSpan.FromHours(8), "UTC+08", "UTC+08");
    private static readonly DateOnly WeekStart = new(2026, 10, 26);
    private readonly ReminderSchedule _schedule = new();

    [Fact]
    public void ForWeek_UsesThursdayAndFridayLocalTimes()
    {
        var reminders = _schedule.ForWeek(new WeekPeriod(WeekStart), UtcPlusEight);

        Assert.Equal(2, reminders.Count);
        Assert.Equal(ReminderKind.ThursdayAfternoon, reminders[0].Key.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 17, 0, 0, TimeSpan.FromHours(8)), reminders[0].DueAt);
        Assert.Equal(ReminderKind.FridayMorning, reminders[1].Key.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.FromHours(8)), reminders[1].DueAt);
    }

    [Fact]
    public void GetDue_TriggersAtExactThursdayTime()
    {
        var before = new DateTimeOffset(2026, 10, 29, 16, 59, 59, TimeSpan.FromHours(8));
        var atTime = before.AddSeconds(1);

        Assert.Empty(_schedule.GetDue(before, UtcPlusEight, new HashSet<ReminderKey>()));
        var reminder = Assert.Single(_schedule.GetDue(atTime, UtcPlusEight, new HashSet<ReminderKey>()));
        Assert.Equal(ReminderKind.ThursdayAfternoon, reminder.Key.Kind);
    }

    [Fact]
    public void GetDue_IncludesExactlyTwoHoursButNotLater()
    {
        var deadline = new DateTimeOffset(2026, 10, 29, 19, 0, 0, TimeSpan.FromHours(8));

        Assert.Single(_schedule.GetDue(deadline, UtcPlusEight, new HashSet<ReminderKey>()));
        Assert.Empty(_schedule.GetDue(deadline.AddTicks(1), UtcPlusEight, new HashSet<ReminderKey>()));
    }

    [Fact]
    public void DeliveredThursday_DoesNotSuppressFriday()
    {
        var delivered = new HashSet<ReminderKey> { new(WeekStart, ReminderKind.ThursdayAfternoon) };
        var friday = new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.FromHours(8));

        var reminder = Assert.Single(_schedule.GetDue(friday, UtcPlusEight, delivered));

        Assert.Equal(ReminderKind.FridayMorning, reminder.Key.Kind);
    }

    [Fact]
    public void DeliveredReminder_IsNotRepeatedAfterRestartOrClockRewind()
    {
        var delivered = new HashSet<ReminderKey> { new(WeekStart, ReminderKind.ThursdayAfternoon) };
        var time = new DateTimeOffset(2026, 10, 29, 17, 30, 0, TimeSpan.FromHours(8));

        Assert.Empty(_schedule.GetDue(time, UtcPlusEight, delivered));
        Assert.Empty(_schedule.GetDue(time.AddMinutes(-20), UtcPlusEight, delivered));
    }

    [Fact]
    public void GetNext_SkipsDeliveredSlotsAndAdvancesToNextWeek()
    {
        var delivered = new HashSet<ReminderKey>
        {
            new(WeekStart, ReminderKind.ThursdayAfternoon),
            new(WeekStart, ReminderKind.FridayMorning)
        };
        var friday = new DateTimeOffset(2026, 10, 30, 11, 0, 0, TimeSpan.FromHours(8));

        var next = _schedule.GetNext(friday, UtcPlusEight, delivered);

        Assert.Equal(new DateOnly(2026, 11, 2), next.Key.WeekStart);
        Assert.Equal(ReminderKind.ThursdayAfternoon, next.Key.Kind);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 17, 0, 0, TimeSpan.FromHours(8)), next.DueAt);
    }

    [Fact]
    public void InvalidLocalTime_IsShiftedForwardAndMarked()
    {
        var zone = CustomZone(
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 17, 0, 0), 3, 5),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1));
        var week = WeekPeriod.FromDate(new DateOnly(2026, 3, 5));

        var reminder = _schedule.ForWeek(week, zone)[0];

        Assert.Equal(new DateTime(2026, 3, 5, 18, 0, 0), reminder.DueAt.DateTime);
        Assert.True(reminder.Adjustment.HasFlag(ReminderTimeAdjustment.ShiftedInvalidLocalTime));
    }

    [Fact]
    public void AmbiguousLocalTime_UsesEarlierOccurrenceAndMarksIt()
    {
        var zone = CustomZone(
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 18, 0, 0), 11, 5));
        var week = WeekPeriod.FromDate(new DateOnly(2026, 11, 5));

        var reminder = _schedule.ForWeek(week, zone)[0];

        Assert.Equal(new DateTime(2026, 11, 5, 17, 0, 0), reminder.DueAt.DateTime);
        Assert.Equal(TimeSpan.FromHours(1), reminder.DueAt.Offset);
        Assert.True(reminder.Adjustment.HasFlag(ReminderTimeAdjustment.EarlierAmbiguousOccurrence));
    }

    [Fact]
    public void ReminderKey_RejectsInvalidWeekAndKind()
    {
        Assert.Throws<ArgumentException>(() => new ReminderKey(new DateOnly(2026, 10, 30), ReminderKind.FridayMorning));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReminderKey(WeekStart, (ReminderKind)999));
    }

    private static TimeZoneInfo CustomZone(
        TimeZoneInfo.TransitionTime start, TimeZoneInfo.TransitionTime end)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Custom", TimeSpan.Zero, "Custom", "Standard", "Daylight", [rule]);
    }
}
