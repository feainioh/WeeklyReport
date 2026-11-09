using Microsoft.Extensions.Logging;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.App.Services;

public sealed class WeeklyProgressContextProvider(
    IDatabaseConfigurationStore configurationStore,
    IWorkEntryRepository workEntries,
    IGitReportWorkItemSource gitItems,
    IWeeklyReportRepository reports,
    WeeklyReportDraftBuilder draftBuilder,
    TimeProvider timeProvider,
    ILogger<WeeklyProgressContextProvider> logger,
    ILocalStorageStateProvider? localStorage = null) : IWeeklyProgressContextProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(45);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _cacheLock = new();
    private WeeklyProgressContext? _cachedContext;
    private DateTimeOffset _cachedAtUtc;
    private long _generation;

    public async Task<WeeklyProgressContext> GetCurrentWeekAsync(bool forceRefresh = false)
    {
        var now = timeProvider.GetUtcNow();
        var weekStart = WeekPeriod.FromDate(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime)).Start;
        if (!forceRefresh && TryGetCached(weekStart, now, out var cached))
        {
            return cached;
        }

        await _refreshLock.WaitAsync();
        try
        {
            now = timeProvider.GetUtcNow();
            if (!forceRefresh && TryGetCached(weekStart, now, out cached))
            {
                return cached;
            }

            var generation = Volatile.Read(ref _generation);
            var context = await LoadAsync(weekStart);
            lock (_cacheLock)
            {
                if (generation == Volatile.Read(ref _generation))
                {
                    _cachedContext = context;
                    _cachedAtUtc = timeProvider.GetUtcNow();
                }
            }

            return context;
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
            _cachedContext = null;
        }
    }

    private bool TryGetCached(DateOnly weekStart, DateTimeOffset now, out WeeklyProgressContext context)
    {
        lock (_cacheLock)
        {
            if (_cachedContext is not null
                && _cachedContext.WeekStart == weekStart
                && now >= _cachedAtUtc
                && now - _cachedAtUtc < CacheDuration)
            {
                context = _cachedContext;
                return true;
            }
        }

        context = null!;
        return false;
    }

    private async Task<WeeklyProgressContext> LoadAsync(DateOnly weekStart)
    {
        LocalStorageState? localState = null;
        if (localStorage is not null)
        {
            try
            {
                localState = await localStorage.GetStateAsync();
                if (localState.Status is not (LocalStorageStatus.Ready or LocalStorageStatus.Suspended))
                {
                    return Unavailable(weekStart, WeeklyStorageStatus.LocalUnreadable,
                        new InvalidOperationException("本地存储尚未就绪。"));
                }
            }
            catch (DatabaseAccessException exception)
            {
                logger.LogWarning("Weekly progress local storage state failed: {ErrorType}",
                    exception.GetType().Name);
                return Unavailable(weekStart, WeeklyStorageStatus.LocalUnreadable, exception);
            }
        }
        else
        {
            DatabaseConnectionSettings? settings;
            try
            {
                settings = await configurationStore.LoadAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning("Weekly progress configuration failed: {ErrorType}",
                    exception.GetType().Name);
                return Unavailable(weekStart, WeeklyStorageStatus.Unknown, exception);
            }

            if (settings is null)
            {
                return Unavailable(weekStart, WeeklyStorageStatus.MySqlNotConfigured);
            }
        }

        var week = new WeekPeriod(weekStart);
        var manualTask = ReadAsync(() => workEntries.GetByWeekAsync(week), "manual entries");
        var gitTask = ReadAsync(() => gitItems.GetByWeekAsync(week), "Git work items");
        var reportTask = ReadAsync(() => reports.FindByWeekAsync(week), "weekly report");
        await Task.WhenAll(manualTask, gitTask, reportTask);

        var manual = await manualTask;
        var git = await gitTask;
        var report = await reportTask;
        var storage = localState is null
            ? GetStorageStatus(manual.Error, git.Error, report.Error)
            : GetLocalStorageStatus(manual.Error, git.Error, report.Error);

        int? unknownHours = null;
        Exception? aggregationError = null;
        if (manual.Error is null && git.Error is null)
        {
            try
            {
                unknownHours = draftBuilder.Build(week, manual.Value!, git.Value!).UnknownHoursCount;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Weekly progress aggregation failed: {ErrorType}",
                    exception.GetType().Name);
                storage = WeeklyStorageStatus.Unknown;
                aggregationError = exception;
            }
        }

        var errors = new[] { manual.Error, git.Error, report.Error }
            .Where(static error => error is not null)
            .Select(static error => error!.GetType().Name)
            .Concat(aggregationError is null ? [] : [aggregationError.GetType().Name])
            .ToArray();

        return new WeeklyProgressContext(
            weekStart,
            manual.Error is null ? manual.Value!.Count : null,
            git.Error is null ? git.Value!.Count : null,
            report.Error is null ? report.Value is not null : null,
            unknownHours,
            storage,
            0,
            0,
            null,
            errors.Length == 0 ? null : string.Join(", ", errors));
    }

    private async Task<QueryResult<T>> ReadAsync<T>(Func<Task<T>> query, string subject)
    {
        try
        {
            return new QueryResult<T>(await query(), null);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Weekly progress query for {Subject} failed: {ErrorType}",
                subject, exception.GetType().Name);
            return new QueryResult<T>(default, exception);
        }
    }

    private static WeeklyStorageStatus GetStorageStatus(params Exception?[] errors)
    {
        if (errors.All(static error => error is null))
        {
            return WeeklyStorageStatus.MySqlHealthy;
        }

        return errors.Any(static error => error is DatabaseAccessException or InvalidOperationException)
            ? WeeklyStorageStatus.MySqlUnavailable
            : WeeklyStorageStatus.Unknown;
    }

    private static WeeklyStorageStatus GetLocalStorageStatus(params Exception?[] errors)
    {
        if (errors.Any(static error => error is not null))
        {
            return WeeklyStorageStatus.LocalUnreadable;
        }

        return WeeklyStorageStatus.LocalMode;
    }

    private static WeeklyProgressContext Unavailable(
        DateOnly weekStart,
        WeeklyStorageStatus status,
        Exception? exception = null) =>
        new(weekStart, null, null, null, null, status, 0, 0, null,
            exception?.GetType().Name);

    private sealed record QueryResult<T>(T? Value, Exception? Error);
}
