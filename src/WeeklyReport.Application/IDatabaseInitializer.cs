namespace WeeklyReport.Application;

public interface IDatabaseInitializer
{
    Task TestConnectionAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken = default);
    Task InitializeAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken = default);
}
