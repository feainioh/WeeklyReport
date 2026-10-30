namespace WeeklyReport.Application;

public interface IDatabaseConfigurationStore
{
    Task<DatabaseConnectionSettings?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken = default);
}
