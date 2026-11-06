// [新增-机器人交互] 周报保存状态查询测试：已保存/未保存/查询失败，以及 60 秒缓存与失效。
using Microsoft.Extensions.Logging.Abstractions;
using WeeklyReport.App.Services;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.DesktopTests;

public sealed class WeeklyReportStatusProviderTests
{
    private static readonly DateOnly WeekStart = new(2026, 10, 26);

    [Fact]
    public async Task GetWeekAsync_ReturnsNotSaved_WhenRepositoryReturnsNull()
    {
        var provider = CreateProvider(new FakeReports(), new ManualTime());

        Assert.Equal(WeeklyReportSavedState.NotSaved, await provider.GetWeekAsync(WeekStart));
    }

    [Fact]
    public async Task GetWeekAsync_ReturnsSaved_WhenRepositoryReturnsReport()
    {
        var reports = new FakeReports
        {
            Find = _ => Task.FromResult<WeeklyReportSnapshot?>(CreateReport(WeekStart))
        };
        var provider = CreateProvider(reports, new ManualTime());

        Assert.Equal(WeeklyReportSavedState.Saved, await provider.GetWeekAsync(WeekStart));
    }

    [Fact]
    public async Task GetWeekAsync_ReturnsUnknown_WhenRepositoryThrowsExpected()
    {
        var reports = new FakeReports
        {
            Find = _ => throw new InvalidOperationException("synthetic failure")
        };
        var provider = CreateProvider(reports, new ManualTime());

        Assert.Equal(WeeklyReportSavedState.Unknown, await provider.GetWeekAsync(WeekStart));
    }

    [Fact]
    public async Task GetWeekAsync_CachesForSixtySeconds()
    {
        var reports = new FakeReports();
        var clock = new ManualTime();
        var provider = CreateProvider(reports, clock);

        await provider.GetWeekAsync(WeekStart);
        clock.Advance(TimeSpan.FromSeconds(59));
        await provider.GetWeekAsync(WeekStart);
        Assert.Equal(1, reports.Calls);

        clock.Advance(TimeSpan.FromSeconds(2));
        await provider.GetWeekAsync(WeekStart);
        Assert.Equal(2, reports.Calls);
    }

    [Fact]
    public async Task GetWeekAsync_RefreshesCache_WhenClockMovesBackward()
    {
        var reports = new FakeReports();
        var clock = new ManualTime();
        var provider = CreateProvider(reports, clock);

        await provider.GetWeekAsync(WeekStart);
        clock.Set(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero));
        await provider.GetWeekAsync(WeekStart);

        Assert.Equal(2, reports.Calls);
    }

    [Fact]
    public async Task Invalidate_DuringRefresh_PreventsStaleResultFromBeingCached()
    {
        var firstLoad = new TaskCompletionSource<WeeklyReportSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reports = new FakeReports();
        reports.Find = _ => reports.Calls == 1
            ? firstLoad.Task
            : Task.FromResult<WeeklyReportSnapshot?>(null);
        var provider = CreateProvider(reports, new ManualTime());

        var firstRequest = provider.GetWeekAsync(WeekStart);
        provider.Invalidate();
        firstLoad.SetResult(null);
        await firstRequest;
        await provider.GetWeekAsync(WeekStart);

        Assert.Equal(2, reports.Calls);
    }

    [Fact]
    public async Task Invalidate_ForcesRefresh()
    {
        var reports = new FakeReports();
        var provider = CreateProvider(reports, new ManualTime());

        await provider.GetWeekAsync(WeekStart);
        provider.Invalidate();
        await provider.GetWeekAsync(WeekStart);

        Assert.Equal(2, reports.Calls);
    }

    private static WeeklyReportStatusProvider CreateProvider(FakeReports reports, ManualTime clock) =>
        new(reports, clock, NullLogger<WeeklyReportStatusProvider>.Instance);

    private static WeeklyReportSnapshot CreateReport(DateOnly weekStart)
    {
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        return new WeeklyReportSnapshot(Guid.NewGuid(), new WeekPeriod(weekStart),
            "工作", "", "", 0, 0, 0, now, now);
    }

    private sealed class FakeReports : IWeeklyReportRepository
    {
        public int Calls { get; private set; }

        public Func<WeekPeriod, Task<WeeklyReportSnapshot?>> Find { get; set; } =
            _ => Task.FromResult<WeeklyReportSnapshot?>(null);

        public Task<WeeklyReportSnapshot?> FindByWeekAsync(WeekPeriod week)
        {
            Calls++;
            return Find(week);
        }

        public Task<IReadOnlyList<WeeklyReportSnapshot>> ListAsync() =>
            throw new NotSupportedException();

        public Task AddAsync(WeeklyReportSnapshot report) => throw new NotSupportedException();

        public Task<bool> UpdateAsync(WeeklyReportSnapshot report, DateTimeOffset expectedUpdatedAtUtc) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, DateTimeOffset expectedUpdatedAtUtc) =>
            throw new NotSupportedException();
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;

        public void Set(DateTimeOffset value) => _now = value;
    }
}
