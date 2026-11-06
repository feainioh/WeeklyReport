using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public sealed record ReminderFailure(ReminderKey Key, string Message);

public sealed record ReminderEvaluation(
    IReadOnlyList<ScheduledReminder> Sent,
    IReadOnlyList<ReminderFailure> Failures,
    bool NotificationsUnavailable,
    bool HasUncertainPreviousDelivery);

public sealed class ReminderEngine(
    ReminderSchedule schedule,
    IReminderStateStore state,
    IReminderNotifier notifier)
{
    private readonly SemaphoreSlim _evaluationLock = new(1, 1);

    public async Task<ReminderEvaluation> EvaluateAsync(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken = default)
    {
        await _evaluationLock.WaitAsync(cancellationToken);
        try
        {
            var history = await state.LoadAsync(cancellationToken);
            var handled = history.Select(item => item.Key).ToHashSet();
            var due = schedule.GetDue(now, timeZone, handled);
            var uncertain = history.Any(item => item.Status is ReminderDeliveryStatus.Reserved or ReminderDeliveryStatus.Failed);
            if (!await notifier.IsAvailableAsync(cancellationToken))
            {
                return new ReminderEvaluation([], [], true, uncertain);
            }

            if (due.Count == 0)
            {
                return new ReminderEvaluation([], [], false, uncertain);
            }

            var sent = new List<ScheduledReminder>();
            var failures = new List<ReminderFailure>();
            foreach (var reminder in due)
            {
                if (!await state.TryReserveAsync(reminder.Key, cancellationToken))
                {
                    continue;
                }

                try
                {
                    await notifier.SendAsync(reminder, cancellationToken);
                }
                catch (ReminderNotificationException exception)
                {
                    await state.CompleteAsync(reminder.Key, ReminderDeliveryStatus.Failed, CancellationToken.None);
                    failures.Add(new ReminderFailure(reminder.Key, exception.Message));
                    continue;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await state.CompleteAsync(reminder.Key, ReminderDeliveryStatus.Failed, CancellationToken.None);
                    throw;
                }

                await state.CompleteAsync(reminder.Key, ReminderDeliveryStatus.Sent, CancellationToken.None);
                sent.Add(reminder);
            }

            return new ReminderEvaluation(sent.AsReadOnly(), failures.AsReadOnly(), false, uncertain || failures.Count > 0);
        }
        finally
        {
            _evaluationLock.Release();
        }
    }

    public async Task<int> AcknowledgeUncertainAsync(CancellationToken cancellationToken = default)
    {
        await _evaluationLock.WaitAsync(cancellationToken);
        try
        {
            return await state.AcknowledgeUncertainAsync(cancellationToken);
        }
        finally
        {
            _evaluationLock.Release();
        }
    }
}
