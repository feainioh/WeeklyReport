using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Configuration;

public sealed class ProtectedReminderStateStore : IReminderStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    private readonly string _filePath;
    private readonly ISecretProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProtectedReminderStateStore(string filePath, ISecretProtector? protector = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _protector = protector ?? new WindowsDpapiSecretProtector();
    }

    public async Task<IReadOnlyList<ReminderDelivery>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return (await LoadCoreAsync(cancellationToken)).AsReadOnly();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryReserveAsync(ReminderKey key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var deliveries = await LoadCoreAsync(cancellationToken);
            if (deliveries.Any(item => item.Key == key))
            {
                return false;
            }

            deliveries.Add(new ReminderDelivery(key, ReminderDeliveryStatus.Reserved));
            await SaveCoreAsync(deliveries, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAsync(
        ReminderKey key,
        ReminderDeliveryStatus status,
        CancellationToken cancellationToken = default)
    {
        if (status is not (ReminderDeliveryStatus.Sent or ReminderDeliveryStatus.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var deliveries = await LoadCoreAsync(cancellationToken);
            var index = deliveries.FindIndex(item => item.Key == key);
            if (index < 0 || deliveries[index].Status != ReminderDeliveryStatus.Reserved)
            {
                throw new InvalidOperationException("提醒状态已发生变化，请检查本地提醒配置。");
            }

            deliveries[index] = new ReminderDelivery(key, status);
            await SaveCoreAsync(deliveries, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> AcknowledgeUncertainAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var deliveries = await LoadCoreAsync(cancellationToken);
            var changed = 0;
            for (var index = 0; index < deliveries.Count; index++)
            {
                if (deliveries[index].Status is ReminderDeliveryStatus.Reserved or ReminderDeliveryStatus.Failed)
                {
                    deliveries[index] = deliveries[index] with { Status = ReminderDeliveryStatus.Acknowledged };
                    changed++;
                }
            }

            if (changed > 0)
            {
                await SaveCoreAsync(deliveries, cancellationToken);
            }

            return changed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ReminderDelivery>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        var envelope = await AtomicJsonFile.ReadAsync<StoredEnvelope>(_filePath, cancellationToken);
        if (envelope is null)
        {
            return [];
        }

        if (envelope.Version != 1 || string.IsNullOrWhiteSpace(envelope.ProtectedEntries))
        {
            throw new InvalidDataException("本地提醒状态版本或内容无效。");
        }

        try
        {
            var json = _protector.Unprotect(envelope.ProtectedEntries);
            var stored = JsonSerializer.Deserialize<StoredDelivery[]>(json, Options)
                ?? throw new InvalidDataException("本地提醒状态为空。");
            var result = new List<ReminderDelivery>(stored.Length);
            var seen = new HashSet<ReminderKey>();
            foreach (var item in stored)
            {
                if (item is null
                    || !DateOnly.TryParseExact(item.WeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var week)
                    || !Enum.TryParse<ReminderKind>(item.Kind, out var kind)
                    || !Enum.IsDefined(kind) || kind.ToString() != item.Kind
                    || !Enum.TryParse<ReminderDeliveryStatus>(item.Status, out var status)
                    || !Enum.IsDefined(status) || status.ToString() != item.Status)
                {
                    throw new InvalidDataException("本地提醒状态内容无效。");
                }

                ReminderKey key;
                try
                {
                    key = new ReminderKey(week, kind);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException("本地提醒状态的周次无效。", exception);
                }

                if (!seen.Add(key))
                {
                    throw new InvalidDataException("本地提醒状态包含重复记录。");
                }

                result.Add(new ReminderDelivery(key, status));
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("本地提醒状态格式无效。", exception);
        }
    }

    private Task SaveCoreAsync(IReadOnlyCollection<ReminderDelivery> deliveries, CancellationToken cancellationToken)
    {
        var stored = deliveries.Select(item => new StoredDelivery(
            item.Key.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            item.Key.Kind.ToString(),
            item.Status.ToString())).ToArray();
        var json = JsonSerializer.Serialize(stored, Options);
        return AtomicJsonFile.WriteAsync(
            _filePath, new StoredEnvelope(1, _protector.Protect(json)), cancellationToken);
    }

    private sealed record StoredEnvelope(int Version, string ProtectedEntries);
    private sealed record StoredDelivery(string WeekStart, string Kind, string Status);
}
