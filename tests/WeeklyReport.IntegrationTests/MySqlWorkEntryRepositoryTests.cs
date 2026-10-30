using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence;

namespace WeeklyReport.IntegrationTests;

[Collection("MySqlIntegration")]
[Trait("Category", "MySql")]
public class MySqlWorkEntryRepositoryTests
{
    [MySqlIntegrationFact]
    public async Task MigrationAndCrud_WorkWithAnIsolatedMySqlDatabase()
    {
        var settings = MySqlIntegrationSupport.LoadSettings();
        var initializer = new MySqlDatabaseInitializer();
        await initializer.InitializeAsync(settings);
        await initializer.InitializeAsync(settings);

        var repository = new SqlSugarWorkEntryRepository(new MySqlIntegrationSupport.FixedConfigurationStore(settings));
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

}

public sealed class MySqlIntegrationFactAttribute : FactAttribute
{
    public MySqlIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEEKLYREPORT_TEST_CONNECTION")))
        {
            Skip = "需要配置 WEEKLYREPORT_TEST_CONNECTION 指向独立测试数据库。";
        }
    }
}
