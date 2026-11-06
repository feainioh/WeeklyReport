using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence;

namespace WeeklyReport.IntegrationTests;

[Collection("MySqlIntegration")]
[Trait("Category", "MySql")]
// [新增-离线存储] Phase 1 切片 C：接入周报仓储共享契约
public class MySqlWeeklyReportRepositoryTests : WeeklyReportRepositoryContract
{
    [MySqlIntegrationFact]
    public async Task Crud_PreservesSnapshotAndEnforcesOneReportPerWeek()
    {
        var settings = MySqlIntegrationSupport.LoadSettings();
        await new MySqlDatabaseInitializer().InitializeAsync(settings);
        var repository = new SqlSugarWeeklyReportRepository(new MySqlIntegrationSupport.FixedConfigurationStore(settings));
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
            Assert.False(await repository.UpdateAsync(updated, loaded.UpdatedAtUtc));
            Assert.Equal(updated.WorkSummary, (await repository.FindByWeekAsync(week))!.WorkSummary);
            Assert.Contains(await repository.ListAsync(), item => item.Id == report.Id);
            var latest = await repository.FindByWeekAsync(week);
            Assert.NotNull(latest);
            Assert.False(await repository.DeleteAsync(report.Id, loaded.UpdatedAtUtc));
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

    // [新增-离线存储] Phase 1 切片 C：契约钩子与包装（原有测试保持原样）
    protected override Task InitializeAsync() =>
        new MySqlDatabaseInitializer().InitializeAsync(MySqlIntegrationSupport.LoadSettings());

    protected override Task<IWeeklyReportRepository> CreateRepositoryAsync() =>
        Task.FromResult<IWeeklyReportRepository>(new SqlSugarWeeklyReportRepository(
            new MySqlIntegrationSupport.FixedConfigurationStore(MySqlIntegrationSupport.LoadSettings())));

    [MySqlIntegrationFact]
    public async Task Contract_Crud_PreservesSnapshotAndEnforcesOneReportPerWeek() =>
        await Crud_PreservesSnapshotAndEnforcesOneReportPerWeekAsync();
}
