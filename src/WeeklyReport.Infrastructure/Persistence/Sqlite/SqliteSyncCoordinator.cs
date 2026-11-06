// [新增-离线存储] Phase 2 G2：后台同步协调器——定时/手动/启动触发同步，带退避与状态通知。
// 本地优先模式激活时由 App 启动；每 10 秒检查待同步队列（有变更才真正连 MySQL），
// MySQL 不可达时指数退避（最长 5 分钟），恢复后自动重试。手动"立即同步"绕过退避。
using Microsoft.Extensions.Logging;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

public sealed class SqliteSyncCoordinator(
    string databasePath,
    IDatabaseConfigurationStore configurationStore,
    TimeProvider timeProvider,
    Action<string>? reportStatus,
    ILogger<SqliteSyncCoordinator>? logger) : ILocalSyncCoordinator, IAsyncDisposable, IDisposable
{
    private readonly SqliteSyncApplier _applier = new(databasePath, configurationStore);
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly object _disposeSync = new();
    private Task? _loop;
    private Task? _disposeTask;
    private DateTimeOffset _lastAttemptUtc;
    private TimeSpan _backoff = TimeSpan.FromSeconds(30);
    private volatile bool _disposed;

    public event Action<SyncOutcome>? SyncCompleted;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    public async Task<SyncOutcome> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return new SyncOutcome(0, 0, 0, "协调器已停止");
        }

        // [新增-离线存储] SemaphoreSlim 替代 Lock（支持跨 await）
        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
            {
                return new SyncOutcome(0, 0, 0, "协调器已停止");
            }

            return await ExecuteSyncAsync(cancellationToken);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // 启动即同步一次
        await TrySyncAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            await TrySyncAsync(cancellationToken);
        }
    }

    private async Task TrySyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await new SqliteLocalStore(databasePath).GetStateAsync(cancellationToken);
            if (state.Status == LocalStorageStatus.Suspended)
            {
                return;
            }

            var pending = await CountPendingAsync(cancellationToken);
            if (pending == 0)
            {
                return; // 无待同步变更，不连 MySQL
            }

            // 退避检查：距上次尝试不足退避时间则跳过
            var now = timeProvider.GetUtcNow();
            if (now - _lastAttemptUtc < _backoff)
            {
                return;
            }

            if (!await _syncLock.WaitAsync(0, cancellationToken))
            {
                return; // 手动同步正在执行，跳过本轮
            }

            try
            {
                if (_disposed)
                {
                    return;
                }

                var outcome = await ExecuteSyncAsync(cancellationToken);
                if (outcome.Failed > 0)
                {
                    // 指数退避并增加 30 秒基础间隔（30s → 90s → 210s → 300s max）
                    _backoff = _backoff >= TimeSpan.FromMinutes(5)
                        ? TimeSpan.FromMinutes(5)
                        : TimeSpan.FromMinutes(Math.Min(5, _backoff.TotalMinutes * 2 + 0.5));
                }
                else
                {
                    _backoff = TimeSpan.FromSeconds(30); // 成功后重置
                }
            }
            finally
            {
                _syncLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger?.LogWarning("Background sync failed: {ErrorType}", exception.GetType().Name);
        }
    }

    private async Task<SyncOutcome> ExecuteSyncAsync(CancellationToken cancellationToken)
    {
        _lastAttemptUtc = timeProvider.GetUtcNow();
        try
        {
            var outcome = await _applier.SyncNowAsync(cancellationToken);
            if (outcome.Applied > 0)
            {
                reportStatus?.Invoke($"已同步 {outcome.Applied} 项变更到 MySQL。");
            }
            else if (outcome.Failed > 0 && outcome.Error is not null)
            {
                reportStatus?.Invoke($"同步失败：{outcome.Error}");
            }

            SyncCompleted?.Invoke(outcome);
            return outcome;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var outcome = new SyncOutcome(0, 0, 1, exception.Message);
            reportStatus?.Invoke($"同步异常：{exception.Message}");
            SyncCompleted?.Invoke(outcome);
            return outcome;
        }
    }

    private async Task<int> CountPendingAsync(CancellationToken cancellationToken)
    {
        await SqliteDatabase.EnsureInitializedAsync(databasePath);
        await using var connection = SqliteDatabase.Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sync_operation WHERE status = 'Pending'";
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        _stop.Cancel();

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        await _syncLock.WaitAsync().ConfigureAwait(false);
        _syncLock.Release();
        _loop?.Dispose();
        _stop.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
