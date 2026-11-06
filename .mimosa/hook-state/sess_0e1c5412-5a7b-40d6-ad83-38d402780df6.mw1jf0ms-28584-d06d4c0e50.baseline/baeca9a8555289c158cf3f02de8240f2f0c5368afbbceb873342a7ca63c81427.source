using WeeklyReport.App.Services;

namespace WeeklyReport.DesktopTests;

public sealed class WorkRhythmReminderPolicyTests
{
    private static readonly DateTimeOffset MondayAtTen =
        new(2026, 3, 16, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void GetDueTime_WhenCrossingScheduledTime_ReturnsOccurrence()
    {
        var due = WorkRhythmReminderPolicy.GetDueTime(
            MondayAtTen.AddSeconds(-20),
            MondayAtTen.AddSeconds(20),
            new WorkRhythmReminderSettings { Enabled = true });

        Assert.Equal(MondayAtTen, due);
    }

    [Theory]
    [InlineData(8, 59)]
    [InlineData(12, 0)]
    [InlineData(12, 59)]
    [InlineData(18, 0)]
    public void IsWorkingTime_OutsideConfiguredHours_ReturnsFalse(int hour, int minute)
    {
        var value = new DateTimeOffset(2026, 3, 16, hour, minute, 0, TimeSpan.FromHours(8));

        Assert.False(WorkRhythmReminderPolicy.IsWorkingTime(value));
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(11, 59)]
    [InlineData(13, 0)]
    [InlineData(17, 59)]
    public void IsWorkingTime_InsideConfiguredHours_ReturnsTrue(int hour, int minute)
    {
        var value = new DateTimeOffset(2026, 3, 16, hour, minute, 0, TimeSpan.FromHours(8));

        Assert.True(WorkRhythmReminderPolicy.IsWorkingTime(value));
    }

    [Fact]
    public void GetDueTime_DoesNotScheduleOnWeekend()
    {
        var previous = new DateTimeOffset(2026, 3, 21, 9, 59, 40, TimeSpan.FromHours(8));

        var due = WorkRhythmReminderPolicy.GetDueTime(
            previous,
            previous.AddSeconds(40),
            new WorkRhythmReminderSettings { Enabled = true });

        Assert.Null(due);
    }

    [Fact]
    public void GetDueTime_SkipsMissedTimeAfterLongPause()
    {
        var due = WorkRhythmReminderPolicy.GetDueTime(
            MondayAtTen.AddMinutes(-5),
            MondayAtTen.AddMinutes(1),
            new WorkRhythmReminderSettings { Enabled = true });

        Assert.Null(due);
    }

    [Fact]
    public void GetDueTime_ReturnsSnoozedReminderDuringWorkingTime()
    {
        var snooze = MondayAtTen.AddMinutes(10);

        var due = WorkRhythmReminderPolicy.GetDueTime(
            snooze.AddSeconds(-20),
            snooze.AddSeconds(20),
            new WorkRhythmReminderSettings { Enabled = true, SnoozedUntil = snooze });

        Assert.Equal(snooze, due);
    }

    [Fact]
    public void GetDueTime_MergesSnoozeWithNearbyScheduledReminder()
    {
        var snooze = MondayAtTen.AddMinutes(58);

        var due = WorkRhythmReminderPolicy.GetDueTime(
            snooze.AddSeconds(-20),
            snooze.AddSeconds(20),
            new WorkRhythmReminderSettings { Enabled = true, SnoozedUntil = snooze });

        Assert.Null(due);
    }

    [Fact]
    public void GetDueTime_SkipsSnoozeOutsideWorkingTime()
    {
        var snooze = new DateTimeOffset(2026, 3, 16, 18, 5, 0, TimeSpan.FromHours(8));

        var due = WorkRhythmReminderPolicy.GetDueTime(
            snooze.AddSeconds(-20),
            snooze.AddSeconds(20),
            new WorkRhythmReminderSettings { Enabled = true, SnoozedUntil = snooze });

        Assert.Null(due);
    }

    [Fact]
    public void GetDueTime_DoesNotRepeatLastHandledReminder()
    {
        var due = WorkRhythmReminderPolicy.GetDueTime(
            MondayAtTen.AddSeconds(-20),
            MondayAtTen.AddSeconds(20),
            new WorkRhythmReminderSettings { Enabled = true, LastReminderAt = MondayAtTen });

        Assert.Null(due);
    }

    [Fact]
    public void GetDueTime_DoesNotRemindWhenDisabled()
    {
        var due = WorkRhythmReminderPolicy.GetDueTime(
            MondayAtTen.AddSeconds(-20),
            MondayAtTen.AddSeconds(20),
            WorkRhythmReminderSettings.Default);

        Assert.Null(due);
    }

    [Theory]
    [InlineData("action=work-rhythm&intent=acknowledge", WorkRhythmReminderAction.Acknowledge)]
    [InlineData("action=work-rhythm&intent=snooze", WorkRhythmReminderAction.Snooze)]
    public void Activation_ParsesSupportedActions(string arguments, WorkRhythmReminderAction expected)
    {
        Assert.True(WorkRhythmReminderActivation.TryParse(arguments, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("action=weekly-report&intent=snooze")]
    [InlineData("action=work-rhythm&intent=unknown")]
    [InlineData("action=work-rhythm&intent=snooze&extra=value")]
    public void Activation_RejectsInvalidArguments(string? arguments)
    {
        Assert.False(WorkRhythmReminderActivation.TryParse(arguments, out _));
    }
}
