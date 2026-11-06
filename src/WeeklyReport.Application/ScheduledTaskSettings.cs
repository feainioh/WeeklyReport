namespace WeeklyReport.Application;

public enum ScheduledTaskKind
{
    GitDailyItems,
    WeeklyDraft
}

public sealed record ScheduledTaskOptions(
    bool Enabled,
    TimeOnly TimeOfDay,
    DayOfWeek? Weekday = null,
    DateOnly? LastAttemptDate = null,
    DateOnly? LastSuccessfulDate = null,
    string? LastResult = null);

public sealed record ScheduledTaskSettings
{
    public ScheduledTaskOptions GitDailyItems { get; init; } =
        new(false, new TimeOnly(17, 30));

    public ScheduledTaskOptions WeeklyDraft { get; init; } =
        new(false, new TimeOnly(16, 0), DayOfWeek.Thursday);

    public static ScheduledTaskSettings Default => new();

    public ScheduledTaskOptions Get(ScheduledTaskKind kind) => kind switch
    {
        ScheduledTaskKind.GitDailyItems => GitDailyItems,
        ScheduledTaskKind.WeeklyDraft => WeeklyDraft,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的定时任务类型。")
    };

    public ScheduledTaskSettings Set(ScheduledTaskKind kind, ScheduledTaskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return kind switch
        {
            ScheduledTaskKind.GitDailyItems => this with { GitDailyItems = options },
            ScheduledTaskKind.WeeklyDraft => this with { WeeklyDraft = options },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的定时任务类型。")
        };
    }
}

public enum ScheduledTaskOutcomeKind
{
    Success,
    NoChanges,
    Skipped,
    Failed
}

public sealed record ScheduledTaskOutcome(
    ScheduledTaskOutcomeKind Kind,
    string Message,
    int ChangedCount = 0)
{
    public static ScheduledTaskOutcome Success(string message, int changedCount = 0) =>
        new(ScheduledTaskOutcomeKind.Success, message, changedCount);

    public static ScheduledTaskOutcome NoChanges(string message) =>
        new(ScheduledTaskOutcomeKind.NoChanges, message);

    public static ScheduledTaskOutcome Skipped(string message) =>
        new(ScheduledTaskOutcomeKind.Skipped, message);

    public static ScheduledTaskOutcome Failed(string message) =>
        new(ScheduledTaskOutcomeKind.Failed, message);
}

public interface IScheduledTaskSettingsStore
{
    Task<ScheduledTaskSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ScheduledTaskSettings settings, CancellationToken cancellationToken = default);
}

public interface IScheduledTaskRunner
{
    Task<ScheduledTaskOutcome> RunAsync(
        ScheduledTaskKind kind,
        DateOnly date,
        CancellationToken cancellationToken = default);
}
