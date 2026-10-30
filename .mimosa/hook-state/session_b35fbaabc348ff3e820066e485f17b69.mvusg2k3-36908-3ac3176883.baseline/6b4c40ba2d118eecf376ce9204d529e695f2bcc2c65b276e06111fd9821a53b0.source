using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Configuration;

namespace WeeklyReport.IntegrationTests;

public class ProtectedReminderStateStoreTests
{
    [Fact]
    public async Task State_IsProtectedAndPersistsAcrossInstances()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "reminders.json");
        try
        {
            var key = new ReminderKey(new DateOnly(2026, 10, 26), ReminderKind.ThursdayAfternoon);
            var first = new ProtectedReminderStateStore(path);
            Assert.True(await first.TryReserveAsync(key));
            Assert.False(await first.TryReserveAsync(key));
            await first.CompleteAsync(key, ReminderDeliveryStatus.Sent);

            var text = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("2026-10-26", text, StringComparison.Ordinal);
            var second = new ProtectedReminderStateStore(path);
            var saved = Assert.Single(await second.LoadAsync());
            Assert.Equal(key, saved.Key);
            Assert.Equal(ReminderDeliveryStatus.Sent, saved.Status);
            Assert.False(await second.TryReserveAsync(key));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task ConcurrentReservations_AllowOnlyOne()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "reminders.json");
        try
        {
            var key = new ReminderKey(new DateOnly(2026, 10, 26), ReminderKind.FridayMorning);
            var store = new ProtectedReminderStateStore(path);

            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.TryReserveAsync(key)));

            Assert.Single(results, result => result);
            Assert.Single(await store.LoadAsync());
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task InvalidState_IsReported()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "reminders.json");
        try
        {
            await File.WriteAllTextAsync(path, "{invalid json}");
            var store = new ProtectedReminderStateStore(path);

            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Acknowledge_PersistsDeduplicationWithoutClaimingDelivery()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "reminders.json");
        try
        {
            var failed = new ReminderKey(new DateOnly(2026, 10, 26), ReminderKind.ThursdayAfternoon);
            var reserved = new ReminderKey(new DateOnly(2026, 10, 26), ReminderKind.FridayMorning);
            var store = new ProtectedReminderStateStore(path);
            Assert.True(await store.TryReserveAsync(failed));
            await store.CompleteAsync(failed, ReminderDeliveryStatus.Failed);
            Assert.True(await store.TryReserveAsync(reserved));

            Assert.Equal(2, await store.AcknowledgeUncertainAsync());
            var reloaded = new ProtectedReminderStateStore(path);
            Assert.All(await reloaded.LoadAsync(),
                item => Assert.Equal(ReminderDeliveryStatus.Acknowledged, item.Status));
            Assert.False(await reloaded.TryReserveAsync(failed));
            Assert.False(await reloaded.TryReserveAsync(reserved));
            Assert.Equal(0, await reloaded.AcknowledgeUncertainAsync());
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
