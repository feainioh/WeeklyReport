// [新增-机器人交互] 本周周报保存状态查询：封装仓储查询并缓存 60 秒；
// 数据库不可用/未配置时返回 Unknown，由调用方显示通用提醒，不向用户暴露异常。
using Microsoft.Extensions.Logging;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.App.Services;

public sealed class WeeklyReportStatusProvider(
    IWeeklyReportRepository reports,
    TimeProvider timeProvider,
    ILogger<WeeklyReportStatusProvider> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private DateOnly? _cachedWeek;
    private WeeklyReportSavedState _cachedState;
    private DateTimeOffset _cachedAtUtc;
    private long _generation;
    private readonly object _cacheLock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<WeeklyReportSavedState> GetWeekAsync(DateOnly weekStart)
    {
        var now = timeProvider.GetUtcNow();
        if (TryGetCached(weekStart, now, out var cached))
        {
            return cached;
        }

        await _refreshLock.WaitAsync();
        try
        {
            now = timeProvider.GetUtcNow();
            if (TryGetCached(weekStart, now, out cached))
            {
                return cached;
            }

            var generation = Volatile.Read(ref _generation);
            WeeklyReportSavedState state;
            try
            {
                var report = await reports.FindByWeekAsync(new WeekPeriod(weekStart));
                state = report is null
                    ? WeeklyReportSavedState.NotSaved
                    : WeeklyReportSavedState.Saved;
            }
            catch (Exception exception) when (exception is not OperationCanceledException
                                            && ErrorMessages.IsExpected(exception))
            {
                logger.LogWarning("Weekly report status check failed: {ErrorType}", exception.GetType().Name);
                return WeeklyReportSavedState.Unknown;
            }

            lock (_cacheLock)
            {
                if (generation == Volatile.Read(ref _generation))
                {
                    _cachedWeek = weekStart;
                    _cachedState = state;
                    _cachedAtUtc = timeProvider.GetUtcNow();
                }
            }

            return state;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        lock (_cacheLock)
        {
            _cachedWeek = null;
        }
    }

    private bool TryGetCached(
        DateOnly weekStart, DateTimeOffset now, out WeeklyReportSavedState state)
    {
        lock (_cacheLock)
        {
            if (_cachedWeek == weekStart
                && now >= _cachedAtUtc
                && now - _cachedAtUtc < CacheDuration)
            {
                state = _cachedState;
                return true;
            }
        }

        state = default;
        return false;
    }
}
