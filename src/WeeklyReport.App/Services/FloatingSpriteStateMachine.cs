// [新增-悬浮精灵] 精灵状态机：纯逻辑、无 UI 依赖，注入 TimeProvider 便于测试（与项目提醒调度一致的时钟模式）。
// 状态迁移规则见 docs/悬浮精灵改造计划.md 第 3 节：任意状态可进入 Dragging；
// Alerted 8 秒、Celebrating 2.5 秒后自动回落 Idle；StateChanged 可能来自计时器线程，订阅方需自行调度回 UI 线程。
namespace WeeklyReport.App.Services;

public enum FloatingSpriteState
{
    Idle,
    Dragging,
    Alerted,
    Celebrating
}

public enum SpriteAlertKind
{
    Strong,
    Soft
}

public sealed class FloatingSpriteStateMachine : IDisposable
{
    private static readonly TimeSpan AlertedDuration = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CelebratingDuration = TimeSpan.FromSeconds(2.5);

    private readonly TimeProvider _timeProvider;
    private ITimer? _fallbackTimer;
    private bool _disposed;

    public FloatingSpriteStateMachine(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public FloatingSpriteState State { get; private set; } = FloatingSpriteState.Idle;

    public SpriteAlertKind AlertKind { get; private set; } = SpriteAlertKind.Strong;

    public event Action<FloatingSpriteState>? StateChanged;

    public void BeginDrag()
    {
        Transition(FloatingSpriteState.Dragging);
    }

    public void EndDrag()
    {
        if (State == FloatingSpriteState.Dragging)
        {
            Transition(FloatingSpriteState.Idle);
        }
    }

    public void Notify(SpriteAlertKind kind = SpriteAlertKind.Strong)
    {
        AlertKind = kind;
        Transition(FloatingSpriteState.Alerted);
        _fallbackTimer = _timeProvider.CreateTimer(
            _ => Transition(FloatingSpriteState.Idle), null, AlertedDuration, Timeout.InfiniteTimeSpan);
    }

    public void Celebrate()
    {
        Transition(FloatingSpriteState.Celebrating);
        _fallbackTimer = _timeProvider.CreateTimer(
            _ => Transition(FloatingSpriteState.Idle), null, CelebratingDuration, Timeout.InfiniteTimeSpan);
    }

    public void Dismiss()
    {
        Transition(FloatingSpriteState.Idle);
    }

    private void Transition(FloatingSpriteState next)
    {
        if (_disposed)
        {
            return;
        }

        _fallbackTimer?.Dispose();
        _fallbackTimer = null;
        if (State == next)
        {
            return;
        }

        State = next;
        StateChanged?.Invoke(next);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fallbackTimer?.Dispose();
        _fallbackTimer = null;
    }
}
