using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WeeklyReport.Application;

namespace WeeklyReport.App.Services;

public sealed class ScheduledTaskMonitor(
    IScheduledTaskSettingsStore settingsStore,
    ScheduledTaskScheduler scheduler,
    ScheduledGitSyncRunner gitRunner,
    ScheduledWeeklyDraftRunner weeklyDraftRunner,
    TimeProvider timeProvider,
    Action<ScheduledTaskKind, ScheduledTaskOutcome> reportResult,
    ILogger<ScheduledTaskMonitor> logger) : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MissedRunGrace = TimeSpan.FromMinutes(2);
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private Task? _loop;
    private DateTime _startedAt;
    private long _lastResumeTicks;
    private long _lastTimeChangeTicks;
    private int _stopRequested;
    private int _resourcesDisposed;
    private int _isExecuting;
    private ScheduledTaskSettings _settings = ScheduledTaskSettings.Default;

    public event Action<ScheduledTaskSettings>? SettingsChanged;

    public ScheduledTaskSettings Settings => _settings;
    public bool IsExecuting => Volatile.Read(ref _isExecuting) != 0;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_loop is not null)
        {
            throw new InvalidOperationException("定时任务监控器已启动。");
        }

        _settings = await settingsStore.LoadAsync(cancellationToken);
        SettingsChanged?.Invoke(_settings);
    }

    public void Start()
    {
        if (_loop is not null)
        {
            throw new InvalidOperationException("定时任务监控器已经启动。");
        }

        _startedAt = timeProvider.GetLocalNow().DateTime;
        SystemEvents.TimeChanged += OnTimeChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    public async Task UpdateSettingsAsync(
        ScheduledTaskSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
            _settings = settings;
        }
        finally
        {
            _executionGate.Release();
        }

        SettingsChanged?.Invoke(settings);
        Signal();
    }

    public async Task<ScheduledTaskOutcome> RunNowAsync(
        ScheduledTaskKind kind,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的定时任务类型。");
        }

        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            Volatile.Write(ref _isExecuting, 1);
            var date = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
            var outcome = await RunTaskAsync(kind, date, cancellationToken);
            var reported = await RecordOutcomeAsync(kind, date, outcome, cancellationToken);
            reportResult(kind, reported);
            return reported;
        }
        finally
        {
            Volatile.Write(ref _isExecuting, 0);
            _executionGate.Release();
        }
    }

    public Task CheckDueTasksAsync(CancellationToken cancellationToken = default)
    {
        if (_loop is null)
        {
            throw new InvalidOperationException("定时任务监控器尚未启动。");
        }

        return ProcessDueTasksAsync(cancellationToken);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueTasksAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Scheduled task monitor iteration failed: {ErrorType}", exception.GetType().Name);
                reportResult(
                    ScheduledTaskKind.GitDailyItems,
                    ScheduledTaskOutcome.Failed("定时任务检查失败，请检查应用日志。"));
            }

            try
            {
                await _signal.WaitAsync(CheckInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessDueTasksAsync(CancellationToken cancellationToken)
    {
        var localNow = timeProvider.GetLocalNow().DateTime;
        var dueTasks = scheduler.Evaluate(_settings, localNow);
        foreach (var kind in dueTasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _executionGate.WaitAsync(cancellationToken);
            try
            {
                Volatile.Write(ref _isExecuting, 1);
                localNow = timeProvider.GetLocalNow().DateTime;
                if (!scheduler.Evaluate(_settings, localNow).Contains(kind))
                {
                    continue;
                }

                var date = DateOnly.FromDateTime(localNow);
                var scheduledAt = date.ToDateTime(_settings.Get(kind).TimeOfDay);
                var resumeTicks = Interlocked.Read(ref _lastResumeTicks);
                var timeChangeTicks = Interlocked.Read(ref _lastTimeChangeTicks);
                var wasMissed = scheduledAt <= _startedAt
                    || localNow - scheduledAt > MissedRunGrace
                    || resumeTicks > _startedAt.Ticks && scheduledAt.Ticks <= resumeTicks
                    || timeChangeTicks > _startedAt.Ticks && scheduledAt.Ticks <= timeChangeTicks;
                var outcome = wasMissed
                    ? ScheduledTaskOutcome.Skipped("应用未运行或设备休眠期间错过了计划时间，本次已跳过。")
                    : await RunTaskAsync(kind, date, cancellationToken);
                var reported = await RecordOutcomeAsync(kind, date, outcome, cancellationToken);
                reportResult(kind, reported);
            }
            finally
            {
                Volatile.Write(ref _isExecuting, 0);
                _executionGate.Release();
            }
        }
    }

    private async Task<ScheduledTaskOutcome> RunTaskAsync(
        ScheduledTaskKind kind,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        try
        {
            return await (kind switch
            {
                ScheduledTaskKind.GitDailyItems => gitRunner.RunAsync(kind, date, cancellationToken),
                ScheduledTaskKind.WeeklyDraft => weeklyDraftRunner.RunAsync(kind, date, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的定时任务类型。")
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("Scheduled task {TaskKind} failed: {ErrorType}", kind, exception.GetType().Name);
            return ScheduledTaskOutcome.Failed("任务执行失败，请检查应用日志。");
        }
    }

    private async Task<ScheduledTaskOutcome> RecordOutcomeAsync(
        ScheduledTaskKind kind,
        DateOnly date,
        ScheduledTaskOutcome outcome,
        CancellationToken cancellationToken)
    {
        var updated = scheduler.RecordAttempt(_settings, kind, date, outcome);
        _settings = updated;
        SettingsChanged?.Invoke(updated);
        try
        {
            await settingsStore.SaveAsync(updated, cancellationToken);
            return outcome;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                "Scheduled task {TaskKind} result could not be persisted: {ErrorType}",
                kind, exception.GetType().Name);
            var failed = ScheduledTaskOutcome.Failed(
                "任务已执行，但执行状态无法保存；请检查本地配置目录。");
            _settings = updated.Set(kind, updated.Get(kind) with { LastResult = failed.Message });
            SettingsChanged?.Invoke(_settings);
            return failed;
        }
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        TimeZoneInfo.ClearCachedData();
        Interlocked.Exchange(ref _lastTimeChangeTicks, timeProvider.GetLocalNow().DateTime.Ticks);
        Signal();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Interlocked.Exchange(ref _lastResumeTicks, timeProvider.GetLocalNow().DateTime.Ticks);
            Signal();
        }
    }

    private void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_loop is not null)
        {
            await _loop;
        }

        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        _stop.Dispose();
        _signal.Dispose();
        _executionGate.Dispose();
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0)
        {
            return;
        }

        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _stop.Cancel();
        Signal();
    }
}
