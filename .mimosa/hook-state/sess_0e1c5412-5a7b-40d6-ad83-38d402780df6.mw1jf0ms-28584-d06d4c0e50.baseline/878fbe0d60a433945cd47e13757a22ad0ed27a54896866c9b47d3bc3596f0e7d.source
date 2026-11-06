using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using WeeklyReport.App.Services;
using WeeklyReport.Application;

namespace WeeklyReport.DesktopTests;

public sealed class ScheduledTaskSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.FilePath);
        var expected = new ScheduledTaskSettings
        {
            GitDailyItems = new ScheduledTaskOptions(
                true, new TimeOnly(18, 15), LastAttemptDate: new DateOnly(2026, 11, 4),
                LastSuccessfulDate: new DateOnly(2026, 11, 3), LastResult: "已同步"),
            WeeklyDraft = new ScheduledTaskOptions(
                true, new TimeOnly(15, 45), DayOfWeek.Friday)
        };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LoadMissingFile_ReturnsDefaults()
    {
        using var directory = new TemporaryDirectory();
        var settings = await CreateStore(directory.FilePath).LoadAsync();

        Assert.Equal(ScheduledTaskSettings.Default, settings);
    }

    [Fact]
    public async Task LoadInvalidJson_ReturnsDefaults()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(directory.FilePath, "{ invalid");

        var settings = await CreateStore(directory.FilePath).LoadAsync();

        Assert.Equal(ScheduledTaskSettings.Default, settings);
    }

    [Fact]
    public async Task LoadLegacyPartialSettings_UsesDefaultValuesForMissingTask()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(directory.FilePath, """{"gitDailyItems":{"enabled":true,"timeOfDay":"18:00:00"}}""");

        var settings = await CreateStore(directory.FilePath).LoadAsync();

        Assert.True(settings.GitDailyItems.Enabled);
        Assert.Equal(new TimeOnly(18, 0), settings.GitDailyItems.TimeOfDay);
        Assert.Equal(ScheduledTaskSettings.Default.WeeklyDraft, settings.WeeklyDraft);
    }

    [Fact]
    public async Task LoadPartialTask_UsesDefaultTimeWhenTimeIsMissing()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(directory.FilePath, """{"gitDailyItems":{"enabled":true}}""");

        var settings = await CreateStore(directory.FilePath).LoadAsync();

        Assert.True(settings.GitDailyItems.Enabled);
        Assert.Equal(new TimeOnly(17, 30), settings.GitDailyItems.TimeOfDay);
    }

    private static JsonScheduledTaskSettingsStore CreateStore(string filePath) =>
        new(filePath, NullLogger<JsonScheduledTaskSettingsStore>.Instance);

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"scheduled-task-test-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(_path);

        public string FilePath => Path.Combine(_path, "settings.json");

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
    }
}
