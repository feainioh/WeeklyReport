// [新增-离线存储] Phase 2 G1：同步闭环测试——MySQL 停止场景不可行（共享服务），改为隔离库闭环：
// 在 MySQL 隔离库上验证安全新增和幂等重试；无基线的更新/删除必须留在冲突队列。
using System.IO;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

[Collection("MySqlIntegration")]
[Trait("Category", "MySql")]
public sealed class SqliteSyncApplierTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"sync-{Guid.NewGuid():N}.db");

    // [新增-SQLite单机] 本类覆盖"保留但停用"的同步闭环（SQLite→MySQL）：按路径注册入队，
    // 使仓储写入像旧版一样自动入队（生产路径不注册、不入队；本类属 MySQL 集成门禁，仅验证保留代码）
    public SqliteSyncApplierTests()
    {
        SqliteOperationQueue.SetEnqueueEnabled(_databasePath, true);
    }

    private async Task<(SqliteWorkEntryRepository Repository, DatabaseConnectionSettings Settings)> SetupAsync()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);
        var settings = MySqlIntegrationSupport.LoadSettings();
        await new MySqlDatabaseInitializer().InitializeAsync(settings);
        var identity = await new MySqlSyncIdentityStore().GetAsync(settings);
        await new SqliteLocalStore(_databasePath).InitializeMirrorAsync(
            [], [], [], [], mirrorIdentity: identity.DatabaseId);
        var repository = new SqliteWorkEntryRepository(_databasePath);
        return (repository, settings);
    }

    private SqliteSyncApplier CreateApplier(DatabaseConnectionSettings settings) =>
        new(_databasePath, new MySqlIntegrationSupport.FixedConfigurationStore(settings));

    [MySqlIntegrationFact]
    public async Task Sync_WorkEntryOperations_AppliesToMySqlAndIsIdempotent()
    {
        var (repository, settings) = await SetupAsync();
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var entryId = Guid.NewGuid();
        var entry = new WorkEntry(entryId, new DateOnly(2026, 11, 5), "同步验证记录", now, now, 45);
        var applier = CreateApplier(settings);

        try
        {
            // 1. 本地写入（自动入队）
            await repository.AddAsync(entry);
            var state = await new SqliteLocalStore(_databasePath).GetStateAsync();
            Assert.Equal(1, state.PendingCount);

            // 2. 同步到 MySQL
            var outcome = await applier.SyncNowAsync();
            // [诊断临时] 打印结果定位问题
            Console.WriteLine($"Sync outcome: Applied={outcome.Applied} Conflicted={outcome.Conflicted} Failed={outcome.Failed} Error={outcome.Error}");
            Assert.Equal(1, outcome.Applied);
            Assert.Equal(0, outcome.Failed);

            // 3. MySQL 侧数据一致
            await using (var connection = MySqlIntegrationSupport.CreateConnection(settings))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT content, confirmed_minutes FROM work_entries WHERE id = @id";
                command.Parameters.AddWithValue("@id", entryId.ToString("D"));
                using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("同步验证记录", reader.GetString(0));
                Assert.Equal(45, reader.GetInt32(1));
            }

            // 4. 幂等：手动将状态改回 Pending 重试（模拟响应丢失）
            await using (var sqlite = SqliteDatabase.Open(_databasePath))
            {
                using var reset = sqlite.CreateCommand();
                reset.CommandText = "UPDATE sync_operation SET status = 'Pending' WHERE status = 'Synced'";
                await reset.ExecuteNonQueryAsync();
            }

            var retry = await applier.SyncNowAsync();
            Assert.Equal(1, retry.Applied); // AlreadyApplied 也计入 Applied（幂等成功）
            Assert.Equal(0, retry.Failed);

            // 5. 更新后再次同步
            var updated = new WorkEntry(entryId, new DateOnly(2026, 11, 5), "同步更新后", now, now.AddMinutes(1), 60);
            await repository.UpdateAsync(updated);
            var sync2 = await applier.SyncNowAsync();
            Assert.Equal(0, sync2.Applied);
            Assert.Equal(1, sync2.Conflicted);

            await using (var connection = MySqlIntegrationSupport.CreateConnection(settings))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT content FROM work_entries WHERE id = @id";
                command.Parameters.AddWithValue("@id", entryId.ToString("D"));
                Assert.Equal("同步验证记录", (string)(await command.ExecuteScalarAsync())!);
            }

            // 6. 没有变更前快照的删除保留为冲突，不修改 MySQL 数据
            await repository.DeleteAsync(entryId);
            var sync3 = await applier.SyncNowAsync();
            Assert.Equal(0, sync3.Applied);
            Assert.Equal(1, sync3.Conflicted);

            await using (var connection = MySqlIntegrationSupport.CreateConnection(settings))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM work_entries WHERE id = @id";
                command.Parameters.AddWithValue("@id", entryId.ToString("D"));
                Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
            }
        }
        finally
        {
            await using var connection = MySqlIntegrationSupport.CreateConnection(settings);
            await connection.OpenAsync();
            foreach (var sql in new[]
            {
                "DELETE FROM work_entries WHERE id = @id",
                "DELETE FROM sync_receipt",
                "DELETE FROM weekly_reports WHERE id = @id"
            })
            {
                using var cleanup = connection.CreateCommand();
                cleanup.CommandText = sql;
                cleanup.Parameters.AddWithValue("@id", entryId.ToString("D"));
                await cleanup.ExecuteNonQueryAsync();
            }
        }
    }

    [MySqlIntegrationFact]
    public async Task Sync_WeeklyReportOperations_AppliesToMySql()
    {
        var (repository, settings) = await SetupAsync();
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var week = new WeekPeriod(new DateOnly(2026, 11, 2));
        var report = new WeeklyReportSnapshot(Guid.NewGuid(), week, "本周同步", "计划", "风险", 60, 30, 0, now, now);
        var reports = new SqliteWeeklyReportRepository(_databasePath);
        var applier = CreateApplier(settings);
        var reportId = report.Id;

        try
        {
            await reports.AddAsync(report);
            var outcome = await applier.SyncNowAsync();
            Assert.Equal(1, outcome.Applied);

            await using var connection = MySqlIntegrationSupport.CreateConnection(settings);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT work_summary FROM weekly_reports WHERE id = @id";
            command.Parameters.AddWithValue("@id", reportId.ToString("D"));
            Assert.Equal("本周同步", (string)(await command.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var connection = MySqlIntegrationSupport.CreateConnection(settings);
            await connection.OpenAsync();
            using var cleanup = connection.CreateCommand();
            cleanup.CommandText = "DELETE FROM weekly_reports WHERE id = @id; DELETE FROM sync_receipt WHERE 1=1";
            cleanup.Parameters.AddWithValue("@id", reportId.ToString("D"));
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    /// <summary>身份不匹配时同步器应暂停并保留队列，且不得改写 MySQL 业务数据。</summary>
    [MySqlIntegrationFact]
    public async Task Sync_WhenDatabaseIdentityDiffers_SuspendsAndKeepsOperationPending()
    {
        var (repository, settings) = await SetupAsync();
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var entry = new WorkEntry(
            Guid.NewGuid(), new DateOnly(2026, 11, 5), "身份不匹配验证", now, now, null);
        await repository.AddAsync(entry);

        await using (var sqlite = SqliteDatabase.Open(_databasePath))
        {
            using var update = sqlite.CreateCommand();
            update.CommandText = "UPDATE local_meta SET mirror_identity = @identity WHERE id = 1";
            update.Parameters.AddWithValue("@identity", Guid.NewGuid().ToString("D"));
            await update.ExecuteNonQueryAsync();
        }

        var outcome = await CreateApplier(settings).SyncNowAsync();
        Assert.Equal(0, outcome.Applied);
        Assert.Equal(1, outcome.Failed);
        Assert.Contains("身份与本地镜像不匹配", outcome.Error);

        var state = await new SqliteLocalStore(_databasePath).GetStateAsync();
        Assert.Equal(LocalStorageStatus.Suspended, state.Status);
        Assert.Contains("身份与本地镜像不匹配", state.StatusReason);
        Assert.Equal(1, state.PendingCount);

        await using var connection = MySqlIntegrationSupport.CreateConnection(settings);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM work_entries WHERE id = @id";
        command.Parameters.AddWithValue("@id", entry.Id.ToString("D"));
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

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
}
