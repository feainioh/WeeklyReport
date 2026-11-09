using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

public sealed class SqliteSyncCoordinatorTests
{
    [Fact]
    public async Task DisposeAsync_StopsBackgroundLoopAndIsIdempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "sync.db");
        var configurationStore = new BlockingDatabaseConfigurationStore();
        SqliteSyncCoordinator? coordinator = null;

        try
        {
            // [新增-SQLite单机] 覆盖保留协调器机械：按路径注册入队以播种待同步操作（生产默认不入队）
            SqliteOperationQueue.SetEnqueueEnabled(databasePath, true);
            await new SqliteLocalStore(databasePath).StartEmptyWorkspaceAsync();
            var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
            await new SqliteWorkEntryRepository(databasePath).AddAsync(
                new WorkEntry(Guid.NewGuid(), new DateOnly(2026, 11, 5), "排队中的同步", now, now));
            coordinator = new SqliteSyncCoordinator(
                databasePath,
                configurationStore,
                TimeProvider.System,
                null,
                NullLogger<SqliteSyncCoordinator>.Instance);
            coordinator.Start();
            await configurationStore.LoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await coordinator.DisposeAsync();
            await coordinator.DisposeAsync();

            var outcome = await coordinator.SyncNowAsync();
            Assert.Equal("协调器已停止", outcome.Error);
        }
        finally
        {
            if (coordinator is not null)
            {
                await coordinator.DisposeAsync();
            }

            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class BlockingDatabaseConfigurationStore : IDatabaseConfigurationStore
    {
        public TaskCompletionSource<bool> LoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DatabaseConnectionSettings?> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public Task SaveAsync(DatabaseConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
