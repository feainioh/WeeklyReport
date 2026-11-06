// [新增-悬浮精灵] 精灵状态机测试：验证状态迁移与超时回落规则（docs/悬浮精灵改造计划.md 第 3 节）。
// ManualTimeProvider 提供可手动推进的时钟与计时器，避免真实等待。
using WeeklyReport.App.Services;

namespace WeeklyReport.DesktopTests;

public sealed class FloatingSpriteStateMachineTests
{
    [Fact]
    public void InitialState_IsIdle()
    {
        using var machine = new FloatingSpriteStateMachine(new ManualTimeProvider());

        Assert.Equal(FloatingSpriteState.Idle, machine.State);
    }

    [Fact]
    public void BeginDrag_FromAnyState_EntersDragging()
    {
        using var machine = new FloatingSpriteStateMachine(new ManualTimeProvider());

        machine.Notify();
        machine.BeginDrag();

        Assert.Equal(FloatingSpriteState.Dragging, machine.State);
    }

    [Fact]
    public void EndDrag_OnlyFromDragging_ReturnsIdle()
    {
        using var machine = new FloatingSpriteStateMachine(new ManualTimeProvider());

        machine.EndDrag();
        Assert.Equal(FloatingSpriteState.Idle, machine.State);

        machine.BeginDrag();
        machine.EndDrag();
        Assert.Equal(FloatingSpriteState.Idle, machine.State);
    }

    [Fact]
    public void Notify_EntersAlerted_AndFallsBackAfterEightSeconds()
    {
        var clock = new ManualTimeProvider();
        using var machine = new FloatingSpriteStateMachine(clock);

        machine.Notify();
        Assert.Equal(FloatingSpriteState.Alerted, machine.State);

        clock.Advance(TimeSpan.FromSeconds(7.9));
        Assert.Equal(FloatingSpriteState.Alerted, machine.State);

        clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal(FloatingSpriteState.Idle, machine.State);
    }

    [Fact]
    public void NotifySoft_RecordsSoftKindAndFallsBackAfterEightSeconds()
    {
        var clock = new ManualTimeProvider();
        using var machine = new FloatingSpriteStateMachine(clock);

        machine.Notify(SpriteAlertKind.Soft);

        Assert.Equal(FloatingSpriteState.Alerted, machine.State);
        Assert.Equal(SpriteAlertKind.Soft, machine.AlertKind);
        clock.Advance(TimeSpan.FromSeconds(8));
        Assert.Equal(FloatingSpriteState.Idle, machine.State);
    }

    [Fact]
    public void NotifyDefault_RecordsStrongKind()
    {
        using var machine = new FloatingSpriteStateMachine(new ManualTimeProvider());

        machine.Notify();

        Assert.Equal(SpriteAlertKind.Strong, machine.AlertKind);
    }

    [Fact]
    public void RepeatedNotify_RestartsFallbackTimer()
    {
        var clock = new ManualTimeProvider();
        using var machine = new FloatingSpriteStateMachine(clock);

        machine.Notify();
        clock.Advance(TimeSpan.FromSeconds(6));
        machine.Notify();
        clock.Advance(TimeSpan.FromSeconds(7.9));

        Assert.Equal(FloatingSpriteState.Alerted, machine.State);
    }

    [Fact]
    public void BeginDrag_CancelsAlertedFallback()
    {
        var clock = new ManualTimeProvider();
        using var machine = new FloatingSpriteStateMachine(clock);

        machine.Notify();
        machine.BeginDrag();
        machine.EndDrag();
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(FloatingSpriteState.Idle, machine.State);
    }

    [Fact]
    public void Celebrate_EntersCelebrating_AndFallsBackAfterTwoAndHalfSeconds()
    {
        var clock = new ManualTimeProvider();
        using var machine = new FloatingSpriteStateMachine(clock);

        machine.Celebrate();
        Assert.Equal(FloatingSpriteState.Celebrating, machine.State);

        clock.Advance(TimeSpan.FromSeconds(2.4));
        Assert.Equal(FloatingSpriteState.Celebrating, machine.State);

        clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal(FloatingSpriteState.Idle, machine.State);
    }

    [Fact]
    public void Dismiss_ReturnsToIdle()
    {
        var clock = new ManualTimeProvider();
        using var machine = new FloatingSpriteStateMachine(clock);

        machine.Notify();
        machine.Dismiss();

        Assert.Equal(FloatingSpriteState.Idle, machine.State);
    }

    [Fact]
    public void StateChanged_IsRaisedOnEveryTransition()
    {
        var clock = new ManualTimeProvider();
        using var machine = new FloatingSpriteStateMachine(clock);
        var observed = new List<FloatingSpriteState>();
        machine.StateChanged += observed.Add;

        machine.BeginDrag();
        machine.EndDrag();
        machine.Notify();
        clock.Advance(TimeSpan.FromSeconds(8));

        Assert.Equal(
            new[] { FloatingSpriteState.Dragging, FloatingSpriteState.Idle, FloatingSpriteState.Alerted, FloatingSpriteState.Idle },
            observed);
    }

    /// <summary>可手动推进的时钟：Advance 时触发到期的计时器回调（同步执行于调用线程）。</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = new();
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration)
        {
            var target = _now + duration;
            while (true)
            {
                var next = _timers.Where(t => t.DueTime.HasValue && t.DueTime.Value <= target).OrderBy(t => t.DueTime).FirstOrDefault();
                if (next is null)
                {
                    break;
                }

                _now = next.DueTime!.Value;
                var invoke = next.Consume();
                if (invoke)
                {
                    next.Callback(next.State);
                }
            }

            _now = target;
        }

        // [新增-悬浮精灵] .NET 10 的 TimeProvider.CreateTimer 为非泛型 TimerCallback 签名
        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(
                callback,
                state,
                _now + dueTime,
                period == Timeout.InfiniteTimeSpan ? null : _now + dueTime + period);
            _timers.Add(timer);
            return timer;
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, DateTimeOffset? dueTime, DateTimeOffset? period) : ITimer
        {
            public TimerCallback Callback { get; } = callback;
            public object? State { get; } = state;
            public DateTimeOffset? DueTime { get; private set; } = dueTime;
            private readonly DateTimeOffset? _period = period;
            private bool _disposed;

            /// <summary>到达到期点；周期性计时器顺延下一到期点，一次性计时器（含已释放）置空。
            /// 必须先清除 DueTime 再判断是否触发，否则已释放计时器会被 Advance 循环反复选中。</summary>
            public bool Consume()
            {
                var invoke = !_disposed;
                DueTime = invoke ? _period : null;
                return invoke;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
                _disposed = true;
                DueTime = null;
            }

            // [新增-悬浮精灵] ITimer 同时要求 IAsyncDisposable（.NET 10）
            public ValueTask DisposeAsync()
            {
                _disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
