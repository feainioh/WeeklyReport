using WeeklyReport.Application;

namespace WeeklyReport.UnitTests;

public sealed class ScheduledTaskSchedulerTests
{
    private readonly ScheduledTaskScheduler _scheduler = new();

    [Fact]
    public void DefaultSettings_AreDisabledWithPlannedTimes()
    {
        var settings = ScheduledTaskSettings.Default;

        Assert.False(settings.GitDailyItems.Enabled);
        Assert.Equal(new TimeOnly(17, 30), settings.GitDailyItems.TimeOfDay);
        Assert.False(settings.WeeklyDraft.Enabled);
        Assert.Equal(new TimeOnly(16, 0), settings.WeeklyDraft.TimeOfDay);
        Assert.Equal(DayOfWeek.Thursday, settings.WeeklyDraft.Weekday);
    }

    [Fact]
    public void Evaluate_ReturnsEnabledDueWeekdayTask()
    {
        var settings = ScheduledTaskSettings.Default with
        {
            GitDailyItems = new ScheduledTaskOptions(true, new TimeOnly(17, 30))
        };

        var due = _scheduler.Evaluate(settings, new DateTime(2026, 11, 4, 17, 30, 0));

        Assert.Equal([ScheduledTaskKind.GitDailyItems], due);
    }

    [Fact]
    public void Evaluate_DoesNotReturnTaskBeforeConfiguredTime()
    {
        var settings = ScheduledTaskSettings.Default with
        {
            GitDailyItems = new ScheduledTaskOptions(true, new TimeOnly(17, 30))
        };

        var due = _scheduler.Evaluate(settings, new DateTime(2026, 11, 4, 17, 29, 59));

        Assert.Empty(due);
    }

    [Theory]
    [InlineData(2026, 11, 7)]
    [InlineData(2026, 11, 8)]
    public void Evaluate_SkipsGitTaskOnWeekend(int year, int month, int day)
    {
        var settings = ScheduledTaskSettings.Default with
        {
            GitDailyItems = new ScheduledTaskOptions(true, new TimeOnly(0, 0))
        };

        var due = _scheduler.Evaluate(settings, new DateTime(year, month, day, 18, 0, 0));

        Assert.Empty(due);
    }

    [Fact]
    public void Evaluate_UsesConfiguredWeeklyDay()
    {
        var settings = ScheduledTaskSettings.Default with
        {
            WeeklyDraft = new ScheduledTaskOptions(true, new TimeOnly(16, 0), DayOfWeek.Wednesday)
        };

        var due = _scheduler.Evaluate(settings, new DateTime(2026, 11, 4, 16, 0, 0));

        Assert.Equal([ScheduledTaskKind.WeeklyDraft], due);
    }

    [Fact]
    public void Evaluate_DoesNotRepeatAttemptedTaskWithinItsPeriod()
    {
        var date = new DateOnly(2026, 11, 4);
        var settings = ScheduledTaskSettings.Default with
        {
            GitDailyItems = new ScheduledTaskOptions(true, TimeOnly.MinValue, LastAttemptDate: date),
            WeeklyDraft = new ScheduledTaskOptions(true, TimeOnly.MinValue, DayOfWeek.Wednesday, LastAttemptDate: date)
        };

        var due = _scheduler.Evaluate(settings, new DateTime(2026, 11, 4, 18, 0, 0));

        Assert.Empty(due);
    }

    [Fact]
    public void Evaluate_AllowsTasksAgainInNextDailyAndWeeklyPeriod()
    {
        var settings = ScheduledTaskSettings.Default with
        {
            GitDailyItems = new ScheduledTaskOptions(
                true, TimeOnly.MinValue, LastAttemptDate: new DateOnly(2026, 11, 4)),
            WeeklyDraft = new ScheduledTaskOptions(
                true, TimeOnly.MinValue, DayOfWeek.Wednesday, LastAttemptDate: new DateOnly(2026, 10, 28))
        };

        var due = _scheduler.Evaluate(settings, new DateTime(2026, 11, 11, 18, 0, 0));

        Assert.Equal([ScheduledTaskKind.GitDailyItems, ScheduledTaskKind.WeeklyDraft], due);
    }

    [Fact]
    public void RecordAttempt_UpdatesAttemptDateAndOnlySuccessfulDateOnSuccess()
    {
        var date = new DateOnly(2026, 11, 4);
        var settings = ScheduledTaskSettings.Default;

        var failed = _scheduler.RecordAttempt(
            settings, ScheduledTaskKind.GitDailyItems, date,
            ScheduledTaskOutcome.Failed("测试失败"));
        var succeeded = _scheduler.RecordAttempt(
            failed, ScheduledTaskKind.GitDailyItems, date.AddDays(1),
            ScheduledTaskOutcome.Success("测试成功"));

        Assert.Equal(date, failed.GitDailyItems.LastAttemptDate);
        Assert.Null(failed.GitDailyItems.LastSuccessfulDate);
        Assert.Equal(date.AddDays(1), succeeded.GitDailyItems.LastAttemptDate);
        Assert.Equal(date.AddDays(1), succeeded.GitDailyItems.LastSuccessfulDate);
        Assert.Equal("测试成功", succeeded.GitDailyItems.LastResult);
    }
}
