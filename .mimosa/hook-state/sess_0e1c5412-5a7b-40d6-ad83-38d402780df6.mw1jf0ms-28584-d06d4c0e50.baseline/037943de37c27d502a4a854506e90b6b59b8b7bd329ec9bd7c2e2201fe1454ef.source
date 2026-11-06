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

    public async Task<WeeklyReportSavedState> GetWeekAsync(DateOnly weekStart)
    {
        var now = timeProvider.GetUtcNow();
        if (_cachedWeek == weekStart && now - _cachedAtUtc < CacheDuration)
        {
            return _cachedState;
        }

        try
        {
            var report = await reports.FindByWeekAsync(new WeekPeriod(weekStart));
            var state = report is null
                ? WeeklyReportSavedState.NotSaved
                : WeeklyReportSavedState.Saved;

            _cachedWeek = weekStart;
            _cachedState = state;
            _cachedAtUtc = now;
            return state;
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                        && ErrorMessages.IsExpected(exception))
        {
            logger.LogWarning("Weekly report status check failed: {ErrorType}", exception.GetType().Name);
            return WeeklyReportSavedState.Unknown;
        }
    }

    public void Invalidate()
    {
        _cachedWeek = null;
    }
}
