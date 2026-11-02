using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Configuration;

public sealed class AiSettingsStore : IAiSettingsStore
{
    private readonly string _filePath;
    private readonly ISecretProtector _protector;

    public AiSettingsStore(string filePath, ISecretProtector? protector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _protector = protector ?? new WindowsDpapiSecretProtector();
    }

    public async Task<AiSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var stored = await AtomicJsonFile.ReadAsync<StoredAiSettings>(_filePath, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (stored.Version != 1 || string.IsNullOrWhiteSpace(stored.ProtectedApiKey))
        {
            throw new InvalidDataException("DeepSeek 配置文件版本或密钥无效。");
        }

        return new AiSettings(stored.Model, _protector.Unprotect(stored.ProtectedApiKey));
    }

    public async Task SaveAsync(AiSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var stored = new StoredAiSettings(1, settings.Model, _protector.Protect(settings.ApiKey));
        await AtomicJsonFile.WriteAsync(_filePath, stored, cancellationToken);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(_filePath);
        return Task.CompletedTask;
    }

    private sealed record StoredAiSettings(int Version, string Model, string ProtectedApiKey);
}
