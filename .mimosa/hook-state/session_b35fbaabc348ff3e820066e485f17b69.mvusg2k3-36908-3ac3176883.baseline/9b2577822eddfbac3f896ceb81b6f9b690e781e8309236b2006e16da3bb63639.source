using System.Text.Json;
using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Configuration;

namespace WeeklyReport.IntegrationTests;

public class AiSettingsStoreTests
{
    [Fact]
    public async Task SaveLoadDelete_ProtectsTheApiKey()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "ai.json");
        try
        {
            var store = new AiSettingsStore(path);
            Assert.Null(await store.LoadAsync());
            await store.SaveAsync(new AiSettings("deepseek-flash", "synthetic-api-key"));

            var contents = await File.ReadAllTextAsync(path);
            var loaded = await store.LoadAsync();
            Assert.DoesNotContain("synthetic-api-key", contents, StringComparison.Ordinal);
            Assert.NotNull(loaded);
            Assert.Equal("deepseek-flash", loaded.Model);
            Assert.Equal("synthetic-api-key", loaded.ApiKey);
            Assert.DoesNotContain("synthetic-api-key", loaded.ToString(), StringComparison.Ordinal);

            await store.DeleteAsync();
            Assert.Null(await store.LoadAsync());
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task CancelledSave_DoesNotOverwriteExistingSettings()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "ai.json");
        try
        {
            var store = new AiSettingsStore(path);
            await store.SaveAsync(new AiSettings("deepseek-flash", "first-synthetic-key"));
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.SaveAsync(new AiSettings("other-model", "second-synthetic-key"), cancellation.Token));

            var loaded = await store.LoadAsync();
            Assert.NotNull(loaded);
            Assert.Equal("deepseek-flash", loaded.Model);
            Assert.Equal("first-synthetic-key", loaded.ApiKey);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task InvalidConfiguration_IsNotSilentlyIgnored()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "ai.json");
        try
        {
            await File.WriteAllTextAsync(path, "{invalid json}");
            var store = new AiSettingsStore(path);

            await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync());
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WeeklyReport.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
