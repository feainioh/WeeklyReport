namespace WeeklyReport.Application;

public enum BackupEntryKind
{
    Manual,
    Git
}

public sealed record BackupProject(Guid Id, string Name);

public sealed record BackupEntry(
    Guid Id,
    BackupEntryKind Kind,
    Guid? ProjectId,
    DateOnly WorkDate,
    string Description,
    int? EstimatedMinutes,
    int? ConfirmedMinutes,
    bool IsDeleted,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record BackupCommit(
    Guid ProjectId,
    Guid WorkItemId,
    string Hash,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    string Subject);

public sealed record BackupReport(
    Guid Id,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    string WorkSummary,
    string NextWeekPlan,
    string Risks,
    int ConfirmedMinutes,
    int EstimatedMinutes,
    int UnknownHoursCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BackupDocument(
    int FormatVersion,
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<BackupProject> Projects,
    IReadOnlyList<BackupEntry> Entries,
    IReadOnlyList<BackupCommit> Commits,
    IReadOnlyList<BackupReport> Reports)
{
    public const int CurrentFormatVersion = 1;
    public override string ToString() => "WeeklyReport backup (business content redacted)";
}
