// [新增-离线存储] Phase 1 切片 C：IWeeklyReportRepository 共享契约——快照字段完整往返、
// 周唯一约束（重复新增拒绝且不覆盖）、updated_at_utc 乐观并发（更新/删除需匹配期望时间戳）、
// 按周查询与倒序列表。基类方法不挂 [Fact]，由子类按各自跳过策略包装。
using System.Security.Cryptography;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.IntegrationTests;

public abstract class WeeklyReportRepositoryContract
{
    protected abstract Task InitializeAsync();

    protected abstract Task<IWeeklyReportRepository> CreateRepositoryAsync();

    protected virtual async Task Crud_PreservesSnapshotAndEnforcesOneReportPerWeekAsync()
    {
        await InitializeAsync();
        var repository = await CreateRepositoryAsync();
        var week = await FindUnusedWeekAsync(repository);
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var report = new WeeklyReportSnapshot(
            Guid.NewGuid(), week, "本周工作 😀", "下周计划", "风险", 90, 45, 1, now, now);
        var inserted = false;

        try
        {
            await repository.AddAsync(report);
            inserted = true;
            var loaded = await repository.FindByWeekAsync(week);
            Assert.NotNull(loaded);
            Assert.Equal(report.Id, loaded.Id);
            Assert.Equal(report.WorkSummary, loaded.WorkSummary);
            Assert.Equal(report.ConfirmedMinutes, loaded.ConfirmedMinutes);
            Assert.Equal(report.EstimatedMinutes, loaded.EstimatedMinutes);
            Assert.Equal(report.UnknownHoursCount, loaded.UnknownHoursCount);

            var duplicate = new WeeklyReportSnapshot(
                Guid.NewGuid(), week, "不能覆盖", "", "", 0, 0, 0, now, now);
            await Assert.ThrowsAsync<DatabaseAccessException>(() => repository.AddAsync(duplicate));
            Assert.Equal(report.WorkSummary, (await repository.FindByWeekAsync(week))!.WorkSummary);

            var updated = new WeeklyReportSnapshot(
                report.Id, week, "修改后的周报", "新计划", "新风险", 90, 45, 1, now, now.AddMinutes(1));
            Assert.True(await repository.UpdateAsync(updated, loaded.UpdatedAtUtc));
            Assert.False(await repository.UpdateAsync(updated, loaded.UpdatedAtUtc)); // 乐观并发：过期时间戳拒绝
            Assert.Equal(updated.WorkSummary, (await repository.FindByWeekAsync(week))!.WorkSummary);
            Assert.Contains(await repository.ListAsync(), item => item.Id == report.Id);
            var latest = await repository.FindByWeekAsync(week);
            Assert.NotNull(latest);
            Assert.False(await repository.DeleteAsync(report.Id, loaded.UpdatedAtUtc)); // 过期时间戳拒绝删除
            Assert.True(await repository.DeleteAsync(report.Id, latest.UpdatedAtUtc));
            inserted = false;
            Assert.Null(await repository.FindByWeekAsync(week));
        }
        finally
        {
            if (inserted)
            {
                var latest = await repository.FindByWeekAsync(week);
                if (latest?.Id == report.Id)
                {
                    await repository.DeleteAsync(report.Id, latest.UpdatedAtUtc);
                }
            }
        }
    }

    protected static async Task<WeekPeriod> FindUnusedWeekAsync(IWeeklyReportRepository repository)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var week = new WeekPeriod(new DateOnly(2000, 1, 3).AddDays(RandomNumberGenerator.GetInt32(0, 5000) * 7));
            if (await repository.FindByWeekAsync(week) is null)
            {
                return week;
            }
        }

        throw new InvalidOperationException("无法在隔离测试库中找到空闲周。");
    }
}
