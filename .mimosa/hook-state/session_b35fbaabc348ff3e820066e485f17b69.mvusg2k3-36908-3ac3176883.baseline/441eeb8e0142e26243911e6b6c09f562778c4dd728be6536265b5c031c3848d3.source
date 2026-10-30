using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Configuration;

public sealed class DatabaseConfigurationStore : IDatabaseConfigurationStore
{
    private readonly string _filePath;
    private readonly ISecretProtector _protector;

    public DatabaseConfigurationStore(string filePath, ISecretProtector? protector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _protector = protector ?? new WindowsDpapiSecretProtector();
    }

    public async Task<DatabaseConnectionSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await AtomicJsonFile.ReadAsync<StoredDatabaseSettings>(_filePath, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (stored.Version != 1 || string.IsNullOrWhiteSpace(stored.ProtectedPassword))
        {
            throw new InvalidDataException("数据库配置文件版本或凭据无效。");
        }

        var password = _protector.Unprotect(stored.ProtectedPassword);
        return new DatabaseConnectionSettings(stored.Host, stored.Port, stored.Database, stored.UserName, password);
    }

    public async Task SaveAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var stored = new StoredDatabaseSettings(
            1, settings.Host, settings.Port, settings.Database, settings.UserName, _protector.Protect(settings.Password));
        await AtomicJsonFile.WriteAsync(_filePath, stored, cancellationToken);
    }

    private sealed record StoredDatabaseSettings(
        int Version,
        string Host,
        int Port,
        string Database,
        string UserName,
        string ProtectedPassword);
}
