using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public class ReminderEngineTests
{
    private static readonly DateOnly WeekStart = new(2026, 10, 26);
    private static readonly DateTimeOffset Thursday = new(2026, 10, 29, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Evaluate_ReservesBeforeSendingAndDoesNotRepeat()
    {
        var state = new TestStateStore();
        var notifier = new TestNotifier();
        var engine = new ReminderEngine(new ReminderSchedule(), state, notifier);

        var first = await engine.EvaluateAsync(Thursday, TimeZoneInfo.Utc);
        var second = await engine.EvaluateAsync(Thursday.AddMinutes(1), TimeZoneInfo.Utc);

        Assert.Single(first.Sent);
        Assert.Empty(second.Sent);
        Assert.Equal(1, notifier.SentCount);
        Assert.Equal(ReminderDeliveryStatus.Sent, Assert.Single(await state.LoadAsync()).Status);
    }

    [Fact]
    public async Task DisabledNotifications_AreReportedWithoutReservingAndCanRecover()
    {
        var state = new TestStateStore();
        var notifier = new TestNotifier { Available = false };
        var engine = new ReminderEngine(new ReminderSchedule(), state, notifier);

        var disabled = await engine.EvaluateAsync(Thursday, TimeZoneInfo.Utc);
        Assert.True(disabled.NotificationsUnavailable);
        Assert.Empty(await state.LoadAsync());

        notifier.Available = true;
        var recovered = await engine.EvaluateAsync(Thursday.AddMinutes(10), TimeZoneInfo.Utc);
        Assert.Single(recovered.Sent);
    }

    [Fact]
    public async Task SendFailure_IsRecordedAndNotRetriedSilently()
    {
        var state = new TestStateStore();
        var notifier = new TestNotifier { FailSend = true };
        var engine = new ReminderEngine(new ReminderSchedule(), state, notifier);

        var first = await engine.EvaluateAsync(Thursday, TimeZoneInfo.Utc);
        var second = await engine.EvaluateAsync(Thursday.AddMinutes(1), TimeZoneInfo.Utc);

        Assert.Single(first.Failures);
        Assert.True(second.HasUncertainPreviousDelivery);
        Assert.Equal(1, notifier.SentCount);
        Assert.Equal(ReminderDeliveryStatus.Failed, Assert.Single(await state.LoadAsync()).Status);
    }

    [Fact]
    public async Task CrashAfterReservation_IsReportedAndNotRepeated()
    {
        var state = new TestStateStore();
        await state.TryReserveAsync(new ReminderKey(WeekStart, ReminderKind.ThursdayAfternoon));
        var notifier = new TestNotifier();
        var engine = new ReminderEngine(new ReminderSchedule(), state, notifier);

        var result = await engine.EvaluateAsync(Thursday.AddMinutes(1), TimeZoneInfo.Utc);

        Assert.True(result.HasUncertainPreviousDelivery);
        Assert.Empty(result.Sent);
        Assert.Equal(0, notifier.SentCount);
    }

    [Fact]
    public async Task ConcurrentEvaluations_SendAtMostOnce()
    {
        var state = new TestStateStore();
        var notifier = new TestNotifier();
        var engine = new ReminderEngine(new ReminderSchedule(), state, notifier);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => engine.EvaluateAsync(Thursday, TimeZoneInfo.Utc)));

        Assert.Equal(1, notifier.SentCount);
        Assert.Single(await state.LoadAsync());
    }

    [Fact]
    public async Task StateWriteFailure_PreventsNotification()
    {
        var state = new TestStateStore { FailReserve = true };
        var notifier = new TestNotifier();
        var engine = new ReminderEngine(new ReminderSchedule(), state, notifier);

        await Assert.ThrowsAsync<IOException>(() => engine.EvaluateAsync(Thursday, TimeZoneInfo.Utc));

        Assert.Equal(0, notifier.SentCount);
    }

    [Fact]
    public async Task CompletionFailure_LeavesReservationAndPreventsDuplicate()
    {
        var state = new TestStateStore { FailCompletion = true };
        var notifier = new TestNotifier();
        var engine = new ReminderEngine(new ReminderSchedule(), state, notifier);

        await Assert.ThrowsAsync<IOException>(() => engine.EvaluateAsync(Thursday, TimeZoneInfo.Utc));
        state.FailCompletion = false;
        var next = await engine.EvaluateAsync(Thursday.AddMinutes(1), TimeZoneInfo.Utc);

        Assert.True(next.HasUncertainPreviousDelivery);
        Assert.Equal(1, notifier.SentCount);
        Assert.Equal(ReminderDeliveryStatus.Reserved, Assert.Single(await state.LoadAsync()).Status);
    }

    [Fact]
    public async Task Acknowledge_ClearsWarningWithoutRepeatingFailedReminder()
    {
        var state = new TestStateStore();
        var key = new ReminderKey(WeekStart, ReminderKind.ThursdayAfternoon);
        await state.TryReserveAsync(key);
        await state.CompleteAsync(key, ReminderDeliveryStatus.Failed);
        var notifier = new TestNotifier();
        var engine = new ReminderEngine(new ReminderSchedule(), state, notifier);

        Assert.Equal(1, await engine.AcknowledgeUncertainAsync());
        var result = await engine.EvaluateAsync(Thursday.AddMinutes(1), TimeZoneInfo.Utc);

        Assert.False(result.HasUncertainPreviousDelivery);
        Assert.Empty(result.Sent);
        Assert.Equal(0, notifier.SentCount);
        Assert.Equal(ReminderDeliveryStatus.Acknowledged, Assert.Single(await state.LoadAsync()).Status);
        Assert.Equal(0, await engine.AcknowledgeUncertainAsync());
    }

    [Fact]
    public async Task Acknowledge_WaitsForInFlightDeliveryToComplete()
    {
        var state = new TestStateStore();
        var notifier = new BlockingNotifier();
        var engine = new ReminderEngine(new ReminderSchedule(), state, notifier);
        var evaluation = engine.EvaluateAsync(Thursday, TimeZoneInfo.Utc);
        await notifier.Started.Task;

        var acknowledgement = engine.AcknowledgeUncertainAsync();
        Assert.False(acknowledgement.IsCompleted);
        notifier.Release.SetResult();

        Assert.Single((await evaluation).Sent);
        Assert.Equal(0, await acknowledgement);
        Assert.Equal(ReminderDeliveryStatus.Sent, Assert.Single(await state.LoadAsync()).Status);
    }

    private sealed class BlockingNotifier : IReminderNotifier
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public async Task SendAsync(ScheduledReminder reminder, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class TestNotifier : IReminderNotifier
    {
        public bool Available { get; set; } = true;
        public bool FailSend { get; init; }
        public int SentCount { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Available);

        public Task SendAsync(ScheduledReminder reminder, CancellationToken cancellationToken = default)
        {
            SentCount++;
            if (FailSend)
            {
                throw new ReminderNotificationException("系统通知不可用。");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestStateStore : IReminderStateStore
    {
        private readonly Dictionary<ReminderKey, ReminderDeliveryStatus> _entries = new();
        public bool FailReserve { get; init; }
        public bool FailCompletion { get; set; }

        public Task<IReadOnlyList<ReminderDelivery>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReminderDelivery>>(
                _entries.Select(item => new ReminderDelivery(item.Key, item.Value)).ToArray());

        public Task<bool> TryReserveAsync(ReminderKey key, CancellationToken cancellationToken = default)
        {
            if (FailReserve)
            {
                throw new IOException("synthetic state failure");
            }

            return Task.FromResult(_entries.TryAdd(key, ReminderDeliveryStatus.Reserved));
        }

        public Task CompleteAsync(
            ReminderKey key, ReminderDeliveryStatus status, CancellationToken cancellationToken = default)
        {
            if (FailCompletion)
            {
                throw new IOException("synthetic state failure");
            }

            _entries[key] = status;
            return Task.CompletedTask;
        }

        public Task<int> AcknowledgeUncertainAsync(CancellationToken cancellationToken = default)
        {
            var keys = _entries.Where(item => item.Value is ReminderDeliveryStatus.Reserved or ReminderDeliveryStatus.Failed)
                .Select(item => item.Key).ToArray();
            foreach (var key in keys)
            {
                _entries[key] = ReminderDeliveryStatus.Acknowledged;
            }

            return Task.FromResult(keys.Length);
        }
    }
}
