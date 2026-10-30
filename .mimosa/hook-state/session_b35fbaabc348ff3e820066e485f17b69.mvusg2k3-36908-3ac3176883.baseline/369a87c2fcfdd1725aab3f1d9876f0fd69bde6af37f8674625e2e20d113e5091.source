using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public interface IReminderNotifier
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task SendAsync(ScheduledReminder reminder, CancellationToken cancellationToken = default);
}

public sealed class ReminderNotificationException : Exception
{
    public ReminderNotificationException(string message) : base(message) { }
    public ReminderNotificationException(string message, Exception innerException) : base(message, innerException) { }
}
