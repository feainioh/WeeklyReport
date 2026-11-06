namespace WeeklyReport.Application;

public sealed class ScheduledTaskScheduler
{
    public IReadOnlyList<ScheduledTaskKind> Evaluate(ScheduledTaskSettings settings, DateTime localNow)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (localNow.Kind == DateTimeKind.Utc)
        {
            throw new ArgumentException("调度时间必须为本地时间。", nameof(localNow));
        }

        var due = new List<ScheduledTaskKind>(2);
        var date = DateOnly.FromDateTime(localNow);
        if (IsDue(settings.GitDailyItems, localNow, date, isWeekly: false)
            && localNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            due.Add(ScheduledTaskKind.GitDailyItems);
        }

        if (IsDue(settings.WeeklyDraft, localNow, date, isWeekly: true))
        {
            due.Add(ScheduledTaskKind.WeeklyDraft);
        }

        return due;
    }

    public ScheduledTaskSettings RecordAttempt(
        ScheduledTaskSettings settings,
        ScheduledTaskKind kind,
        DateOnly date,
        ScheduledTaskOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(outcome);
        var current = settings.Get(kind);
        return settings.Set(kind, current with
        {
            LastAttemptDate = date,
            LastSuccessfulDate = outcome.Kind is ScheduledTaskOutcomeKind.Success or ScheduledTaskOutcomeKind.NoChanges
                ? date
                : current.LastSuccessfulDate,
            LastResult = outcome.Message
        });
    }

    private static bool IsDue(
        ScheduledTaskOptions options,
        DateTime localNow,
        DateOnly date,
        bool isWeekly)
    {
        if (!options.Enabled || localNow.TimeOfDay < options.TimeOfDay.ToTimeSpan())
        {
            return false;
        }

        if (isWeekly && localNow.DayOfWeek != options.Weekday)
        {
            return false;
        }

        if (!isWeekly && localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        if (options.LastAttemptDate is not DateOnly lastAttempt)
        {
            return true;
        }

        return isWeekly
            ? lastAttempt < DateOnly.FromDateTime(localNow).AddDays(-((int)localNow.DayOfWeek + 6) % 7)
            : lastAttempt < date;
    }
}
