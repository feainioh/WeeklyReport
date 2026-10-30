namespace WeeklyReport.Application;

public sealed record BackupImportResult(int Added, int Overwritten, int Skipped);

public interface IBackupStore
{
    Task<BackupDocument> CaptureAsync(DateTimeOffset exportedAtUtc);
    Task<BackupImportResult> ImportAsync(PreparedBackupImport import, BackupConflictPolicy policy);
}
