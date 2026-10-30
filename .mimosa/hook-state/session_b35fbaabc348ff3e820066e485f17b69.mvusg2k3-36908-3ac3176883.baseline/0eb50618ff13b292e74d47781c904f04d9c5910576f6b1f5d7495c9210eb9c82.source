namespace WeeklyReport.Application;

public sealed class BackupService(IBackupStore store, BackupImportPlanner planner, TimeProvider timeProvider)
{
    public Task<BackupDocument> CaptureAsync() => store.CaptureAsync(timeProvider.GetUtcNow().ToUniversalTime());

    public async Task<PreparedBackupImport> PrepareImportAsync(BackupDocument document)
    {
        BackupValidator.Validate(document);
        var current = await CaptureAsync();
        return planner.Prepare(document, current);
    }

    public Task<BackupImportResult> ApplyImportAsync(
        PreparedBackupImport import, BackupConflictPolicy policy, bool userConfirmed)
    {
        ArgumentNullException.ThrowIfNull(import);
        if (!userConfirmed)
        {
            throw new InvalidOperationException("导入前必须由用户确认。");
        }

        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        return store.ImportAsync(import, policy);
    }
}
