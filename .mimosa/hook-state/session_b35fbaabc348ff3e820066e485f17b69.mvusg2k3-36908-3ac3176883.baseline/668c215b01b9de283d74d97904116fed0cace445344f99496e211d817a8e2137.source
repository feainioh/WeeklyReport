namespace WeeklyReport.Application;

public interface IAiSettingsStore
{
    Task<AiSettings?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AiSettings settings, CancellationToken cancellationToken = default);
    Task DeleteAsync(CancellationToken cancellationToken = default);
}
