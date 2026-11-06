namespace WeeklyReport.Application;

public enum WeeklyStorageStatus
{
    MySqlHealthy,
    MySqlNotConfigured,
    MySqlUnavailable,
    LocalMode,
    SyncPending,
    Conflict,
    LocalUnreadable,
    Unknown
}

public sealed record WeeklyProgressContext(
    DateOnly WeekStart,
    int? ManualEntryCount,
    int? GitWorkItemCount,
    bool? HasSavedReport,
    int? UnknownHoursCount,
    WeeklyStorageStatus Storage,
    int PendingSyncCount,
    int ConflictCount,
    DateTimeOffset? LastSyncAtUtc,
    string? Error)
{
    public int? TotalWorkItemCount =>
        ManualEntryCount is null || GitWorkItemCount is null
            ? null
            : ManualEntryCount + GitWorkItemCount;

    public bool IsBusinessDataComplete =>
        ManualEntryCount is not null
        && GitWorkItemCount is not null
        && HasSavedReport is not null
        && UnknownHoursCount is not null;
}

public interface IWeeklyProgressContextProvider
{
    Task<WeeklyProgressContext> GetCurrentWeekAsync(bool forceRefresh = false);
    void Invalidate();
}

public enum WeeklyAdviceKind
{
    StorageUnavailable,
    ConflictPending,
    SyncPending,
    UnknownHours,
    NoWorkItems,
    ReportNotSaved,
    ReportSavedCheck
}

public enum WeeklyAdviceAction
{
    WeeklyReport,
    WorkEntries,
    GitWorkItems,
    SyncStatus,
    StorageSettings,
    None
}

public sealed record WeeklyAdvice(
    WeeklyAdviceKind Kind,
    string Text,
    WeeklyAdviceAction Action,
    string ActionLabel);
