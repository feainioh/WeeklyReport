// [新增-离线存储] Phase 1 切片 A：IWorkEntryRepository 共享契约——MySQL 与 SQLite 实现跑同一组行为测试。
// 契约范围：迁移幂等（初始化两次）、CRUD 与按周查询、周边界（EndExclusive 不含）、中文与表情内容、
// 时间戳微秒精度往返、更新仅变更指定列、删除语义。基类方法不挂 [Fact]，由子类按各自跳过策略包装。
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.IntegrationTests;

public abstract class WorkEntryRepositoryContract
{
    /// <summary>初始化/迁移底层存储（契约要求幂等，子类实现里应执行两次等价调用由场景自带）。</summary>
    protected abstract Task InitializeAsync();

    protected abstract Task<IWorkEntryRepository> CreateRepositoryAsync();

    protected virtual async Task MigrationAndCrudAsync()
    {
        await InitializeAsync();
        await InitializeAsync(); // 迁移幂等：重复初始化不报错、不重复应用
        var repository = await CreateRepositoryAsync();
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var entry = new WorkEntry(Guid.NewGuid(), new DateOnly(2026, 10, 30), "中文与表情 😀", now, now, 75);
        var wasInserted = false;

        try
        {
            await repository.AddAsync(entry);
            wasInserted = true;
            var loaded = await repository.FindAsync(entry.Id);
            Assert.NotNull(loaded);
            Assert.Equal(entry.Content, loaded.Content);
            Assert.Equal(entry.WorkDate, loaded.WorkDate);
            Assert.Equal(entry.CreatedAtUtc, loaded.CreatedAtUtc); // 微秒精度往返（ADR-05）
            Assert.Equal(entry.UpdatedAtUtc, loaded.UpdatedAtUtc);
            Assert.Equal(75, loaded.ConfirmedMinutes);
            Assert.Contains(await repository.GetByWeekAsync(WeekPeriod.FromDate(entry.WorkDate)), item => item.Id == entry.Id);

            var updated = new WorkEntry(entry.Id, entry.WorkDate.AddDays(1), "更新后的工作", now, now.AddMinutes(1), 90);
            Assert.True(await repository.UpdateAsync(updated));
            var loadedUpdate = await repository.FindAsync(entry.Id);
            Assert.Equal(updated.Content, loadedUpdate!.Content);
            Assert.Equal(90, loadedUpdate.ConfirmedMinutes);
            Assert.True(await repository.DeleteAsync(entry.Id));
            wasInserted = false;
            Assert.Null(await repository.FindAsync(entry.Id));
        }
        finally
        {
            if (wasInserted)
            {
                await repository.DeleteAsync(entry.Id);
            }
        }
    }

    protected virtual async Task GetByWeek_ExcludesNextWeekStartAsync()
    {
        await InitializeAsync();
        var repository = await CreateRepositoryAsync();
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var week = WeekPeriod.FromDate(new DateOnly(2026, 10, 30));
        var inWeek = new WorkEntry(Guid.NewGuid(), week.Start, "周内记录", now, now, null);
        var nextWeekStart = new WorkEntry(Guid.NewGuid(), week.EndExclusive, "下一周首日", now, now, null);

        try
        {
            await repository.AddAsync(inWeek);
            await repository.AddAsync(nextWeekStart);
            var loaded = await repository.GetByWeekAsync(week);
            Assert.Contains(loaded, item => item.Id == inWeek.Id);
            Assert.DoesNotContain(loaded, item => item.Id == nextWeekStart.Id);
        }
        finally
        {
            await repository.DeleteAsync(inWeek.Id);
            await repository.DeleteAsync(nextWeekStart.Id);
        }
    }

    // [新增-离线存储] Phase 1 切片 E：全量列举契约（首次镜像数据源）——按日期+主键稳定排序返回全部记录
    protected virtual async Task ListAll_ReturnsEveryEntryOrderedAsync()
    {
        await InitializeAsync();
        var repository = await CreateRepositoryAsync();
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var earlier = new WorkEntry(Guid.NewGuid(), new DateOnly(2026, 10, 26), "上周记录", now, now, null);
        var later = new WorkEntry(Guid.NewGuid(), new DateOnly(2026, 11, 2), "本周记录", now, now, null);

        try
        {
            await repository.AddAsync(later);
            await repository.AddAsync(earlier);
            var all = await repository.ListAllAsync();
            Assert.Contains(all, item => item.Id == earlier.Id);
            Assert.Contains(all, item => item.Id == later.Id);
            var positions = all.Select(item => item.Id).ToArray();
            Assert.True(Array.IndexOf(positions, earlier.Id) < Array.IndexOf(positions, later.Id), "应按工作日期升序排列");
        }
        finally
        {
            await repository.DeleteAsync(earlier.Id);
            await repository.DeleteAsync(later.Id);
        }
    }
}
