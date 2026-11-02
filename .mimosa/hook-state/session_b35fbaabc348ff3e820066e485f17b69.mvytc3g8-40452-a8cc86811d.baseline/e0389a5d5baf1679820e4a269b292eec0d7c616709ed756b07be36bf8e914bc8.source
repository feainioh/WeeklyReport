using System.Security.Cryptography;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence;

namespace WeeklyReport.IntegrationTests;

[Collection("MySqlIntegration")]
[Trait("Category", "MySql")]
public class MySqlWeeklyReportRepositoryTests
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

    private static async Task<WeekPeriod> FindUnusedWeekAsync(IWeeklyReportRepository repository)
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
