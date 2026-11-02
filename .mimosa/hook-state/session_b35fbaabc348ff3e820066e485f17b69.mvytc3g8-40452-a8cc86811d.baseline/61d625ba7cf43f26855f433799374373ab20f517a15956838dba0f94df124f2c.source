using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public enum ReminderDeliveryStatus
{
    Reserved,
    Sent,
    Failed,
    Acknowledged
}

public sealed record ReminderDelivery(ReminderKey Key, ReminderDeliveryStatus Status);

public interface IReminderStateStore
{
    Task<IReadOnlyList<ReminderDelivery>> LoadAsync(CancellationToken cancellationToken = default);
    Task<bool> TryReserveAsync(ReminderKey key, CancellationToken cancellationToken = default);
    Task CompleteAsync(ReminderKey key, ReminderDeliveryStatus status, CancellationToken cancellationToken = default);
    Task<int> AcknowledgeUncertainAsync(CancellationToken cancellationToken = default);
}
