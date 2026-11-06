// [新增-离线存储] Phase 1 切片 B：SQLite Git 同步仓储契约测试——完整事务组/去重/软删除/重算场景在 SQLite 上复跑。
using System.IO;
using System.Text.Json;
using WeeklyReport.Domain;
using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

public sealed class SqliteGitSyncStoreTests : GitSyncStoreContract, IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"gitsync-{Guid.NewGuid():N}.db");

    protected override Task InitializeAsync() => SqliteDatabase.EnsureInitializedAsync(_databasePath);

    protected override Task<IGitProjectRepository> CreateProjectsRepositoryAsync() =>
        Task.FromResult<IGitProjectRepository>(new SqliteGitProjectRepository(_databasePath));

    protected override Task<IGitSyncStore> CreateSyncStoreAsync() =>
        Task.FromResult<IGitSyncStore>(new SqliteGitSyncStore(_databasePath));

    protected override async Task CorruptEstimateAsync(Guid id, int minutes)
    {
        await using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE git_work_items SET estimated_minutes = @minutes WHERE id = @id";
        command.Parameters.AddWithValue("@minutes", minutes);
        command.Parameters.AddWithValue("@id", id.ToString("D"));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    protected override async Task CleanupProjectsAsync(Guid first, Guid second)
    {
        await using var connection = SqliteDatabase.Open(_databasePath);
        foreach (var table in new[] { "git_commit_links", "git_work_items", "git_projects" })
        {
            var column = table == "git_projects" ? "id" : "project_id";
            using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {table} WHERE {column} = @first OR {column} = @second";
            command.Parameters.AddWithValue("@first", first.ToString("D"));
            command.Parameters.AddWithValue("@second", second.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Contract_Sync_IsTransactionalIdempotentAndPreservesManualEdits() =>
        await Sync_IsTransactionalIdempotentAndPreservesManualEditsAsync();

    [Fact]
    public async Task Apply_QueuesGitWorkItemSnapshotBeforeCheckingRemoteConfiguration()
    {
        await InitializeAsync();
        var projects = await CreateProjectsRepositoryAsync();
        var store = await CreateSyncStoreAsync();
        var project = new GitProject(
            Guid.NewGuid(), "快照项目", Path.GetTempPath(), GitAuthorField.Email, "test@example.invalid");
        var day = await FindUnusedDayAsync(store);
        var reader = new TestGitReader();
        reader.Commits[project.Id] = [Commit(project.Id, 1, day, 9, 0)];
        var service = new GitSyncService(reader, store, new GitSyncPlanner(new GitHourEstimator()));
        await projects.AddAsync(project);
        var prepared = await service.PrepareAsync([project], day, day, TimeZoneInfo.Utc);
        await service.ApplyAsync(prepared, true, false, false);

        var applier = new SqliteSyncApplier(_databasePath, new NoDatabaseConfigurationStore());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => applier.SyncNowAsync());
        Assert.Contains("尚未配置 MySQL", exception.Message);
        Assert.Equal(2, (await new SqliteLocalStore(_databasePath).GetStateAsync()).PendingCount);

        string changesJson;
        await using (var connection = SqliteDatabase.Open(_databasePath))
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT changes_json FROM sync_operation WHERE operation_kind = 'git-sync.apply'";
            changesJson = (string)(await command.ExecuteScalarAsync())!;
        }

        var change = Assert.Single(JsonSerializer.Deserialize<LocalOperationChange[]>(changesJson)!);
        using var snapshot = JsonDocument.Parse(change.AfterJson!);
        Assert.Equal(project.Name, snapshot.RootElement.GetProperty("Project").GetProperty("Name").GetString());
        Assert.Single(snapshot.RootElement.GetProperty("WorkItem").GetProperty("Commits").EnumerateArray());
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

    public void Dispose()
    {
        // [新增-离线存储] 清连接池后删除临时库文件（db/-wal/-shm）
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
