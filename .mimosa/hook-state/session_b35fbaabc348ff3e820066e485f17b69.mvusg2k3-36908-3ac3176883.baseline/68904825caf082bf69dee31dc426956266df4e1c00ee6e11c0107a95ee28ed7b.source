using System.IO;
using System.Text.Json;
using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Configuration;

namespace WeeklyReport.IntegrationTests;

public class DatabaseConfigurationStoreTests
{
    [Fact]
    public async Task SaveAndLoad_ProtectsPasswordAndPreservesConfiguration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WeeklyReport.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "database.json");

        try
        {
            var store = new DatabaseConfigurationStore(path);
            Assert.Null(await store.LoadAsync());
            var expected = new DatabaseConnectionSettings("localhost", 3306, "weekly_report", "test-user", "example-secret");

            await store.SaveAsync(expected);
            var storedText = await File.ReadAllTextAsync(path);
            var actual = await store.LoadAsync();

            Assert.DoesNotContain("example-secret", storedText, StringComparison.Ordinal);
            Assert.NotNull(actual);
            Assert.Equal(expected.Host, actual.Host);
            Assert.Equal(expected.Port, actual.Port);
            Assert.Equal(expected.Database, actual.Database);
            Assert.Equal(expected.UserName, actual.UserName);
            Assert.Equal(expected.Password, actual.Password);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Load_InvalidJsonFailsExplicitly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WeeklyReport.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "database.json");

        try
        {
            await File.WriteAllTextAsync(path, "{invalid json}");
            var store = new DatabaseConfigurationStore(path);

            await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync());
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
