// [新增-离线存储] Phase 2：同步安全边界测试——未实现的变更必须进入可见冲突，不得连接 MySQL 或写回执。
using System.IO;
using System.Text.Json;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

/// <summary>验证同步器在没有可验证基线或遇到未知操作时不会连接远程库并伪报成功。</summary>
public sealed class SqliteSyncSafetyTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"sync-safety-{Guid.NewGuid():N}.db");

    /// <summary>未知表操作必须作为可见冲突保存，且不读取数据库配置或写入 MySQL 回执。</summary>
    [Fact]
    public async Task Sync_UnsupportedOperation_PreservesConflictWithoutConnectingToMySql()
    {
        var queue = new SqliteOperationQueue(_databasePath);
        await queue.RunInOperationAsync("backup.import", session =>
        {
            session.Record(new LocalOperationChange(
                "backup_import", "full", "import", LocalObjectChange.Update));
            return Task.FromResult(true);
        });

        var outcome = await new SqliteSyncApplier(_databasePath, new NoDatabaseConfigurationStore())
            .SyncNowAsync();

        Assert.Equal(0, outcome.Applied);
        Assert.Equal(1, outcome.Conflicted);
        Assert.Equal(0, outcome.Failed);

        var state = await new SqliteLocalStore(_databasePath).GetStateAsync();
        Assert.Equal(1, state.ConflictCount);
        Assert.Equal(0, state.PendingCount);

        await using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT reason FROM sync_conflict";
        var reason = (string?)await command.ExecuteScalarAsync();
        Assert.Contains("未知或无效变更", reason);
    }

    /// <summary>没有变更前基线的更新和删除必须保留为冲突，不得自动覆盖远程版本。</summary>
    [Fact]
    public async Task Sync_ChangesWithoutBaseline_PreservesUpdateAndDeleteAsConflicts()
    {
        var queue = new SqliteOperationQueue(_databasePath);
        await queue.RunInOperationAsync("work-entry.edit-delete", session =>
        {
            var rowId = Guid.NewGuid().ToString("D");
            session.Record(new LocalOperationChange(
                "work_entries", rowId, "2026-11-05", LocalObjectChange.Update));
            session.Record(new LocalOperationChange(
                "work_entries", Guid.NewGuid().ToString("D"), "2026-11-05", LocalObjectChange.Delete));
            return Task.FromResult(true);
        });

        var outcome = await new SqliteSyncApplier(_databasePath, new NoDatabaseConfigurationStore())
            .SyncNowAsync();

        Assert.Equal(0, outcome.Applied);
        Assert.Equal(1, outcome.Conflicted);
        Assert.Equal(0, outcome.Failed);

        var state = await new SqliteLocalStore(_databasePath).GetStateAsync();
        Assert.Equal(1, state.ConflictCount);
        Assert.Equal(0, state.PendingCount);
    }

    /// <summary>旧协议队列无法恢复提交时的快照，必须保留冲突且不得读取远程配置。</summary>
    [Fact]
    public async Task Sync_LegacyPayload_IsPreservedAsConflict()
    {
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var entry = new WorkEntry(
            Guid.NewGuid(), new DateOnly(2026, 11, 5), "旧队列", now, now, 15);
        var queue = new SqliteOperationQueue(_databasePath);
        await queue.RunInOperationAsync("work-entry.save", session =>
        {
            session.Record(new LocalOperationChange(
                "work_entries",
                entry.Id.ToString("D"),
                entry.WorkDate.ToString("yyyy-MM-dd"),
                LocalObjectChange.Add)
            {
                AfterJson = JsonSerializer.Serialize(entry)
            });
            return Task.FromResult(true);
        });

        await using (var connection = SqliteDatabase.Open(_databasePath))
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE sync_operation SET payload_version = 1";
            await update.ExecuteNonQueryAsync();
        }

        var outcome = await new SqliteSyncApplier(_databasePath, new NoDatabaseConfigurationStore())
            .SyncNowAsync();

        Assert.Equal(0, outcome.Applied);
        Assert.Equal(1, outcome.Conflicted);
        Assert.Equal(0, outcome.Failed);
        var state = await new SqliteLocalStore(_databasePath).GetStateAsync();
        Assert.Equal(1, state.ConflictCount);
        Assert.Equal(0, state.PendingCount);

        await using var verify = SqliteDatabase.Open(_databasePath);
        using var command = verify.CreateCommand();
        command.CommandText = "SELECT reason FROM sync_conflict";
        Assert.Contains("旧版操作组", (string)(await command.ExecuteScalarAsync())!);
    }

    /// <summary>队列载荷变化但哈希未更新时必须转冲突，避免重放被改写的数据。</summary>
    [Fact]
    public async Task Sync_TamperedSnapshot_IsPreservedAsConflict()
    {
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var entry = new WorkEntry(
            Guid.NewGuid(), new DateOnly(2026, 11, 5), "原始内容", now, now, 15);
        var queue = new SqliteOperationQueue(_databasePath);
        await queue.RunInOperationAsync("work-entry.save", session =>
        {
            session.Record(new LocalOperationChange(
                "work_entries",
                entry.Id.ToString("D"),
                entry.WorkDate.ToString("yyyy-MM-dd"),
                LocalObjectChange.Add)
            {
                AfterJson = JsonSerializer.Serialize(entry)
            });
            return Task.FromResult(true);
        });

        await using (var connection = SqliteDatabase.Open(_databasePath))
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE sync_operation SET changes_json = REPLACE(changes_json, @old, @new)";
            update.Parameters.AddWithValue("@old", "Content");
            update.Parameters.AddWithValue("@new", "ContentX");
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        var outcome = await new SqliteSyncApplier(_databasePath, new NoDatabaseConfigurationStore())
            .SyncNowAsync();

        Assert.Equal(0, outcome.Applied);
        Assert.Equal(1, outcome.Conflicted);
        var state = await new SqliteLocalStore(_databasePath).GetStateAsync();
        Assert.Equal(1, state.ConflictCount);
        Assert.Equal(0, state.PendingCount);
    }

    /// <summary>清理测试创建的 SQLite 文件。</summary>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = _databasePath + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private sealed class NoDatabaseConfigurationStore : IDatabaseConfigurationStore
    {
        public Task<DatabaseConnectionSettings?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DatabaseConnectionSettings?>(null);

        public Task SaveAsync(
            DatabaseConnectionSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
