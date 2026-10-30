namespace WeeklyReport.Application;

public interface IBackupCodec
{
    Task WriteAsync(BackupDocument document, Stream destination, CancellationToken cancellationToken = default);
    Task<BackupDocument> ReadAsync(Stream source, CancellationToken cancellationToken = default);
}
