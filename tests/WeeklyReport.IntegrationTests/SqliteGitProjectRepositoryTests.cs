// [新增-离线存储] Phase 1 切片 B：SQLite Git 项目仓储契约测试——临时库直跑，与 MySQL 同一契约。
using System.IO;
using System.Text.Json;
using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

public sealed class SqliteGitProjectRepositoryTests : GitProjectRepositoryContract, IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"gitprojects-{Guid.NewGuid():N}.db");

    protected override Task InitializeAsync() => SqliteDatabase.EnsureInitializedAsync(_databasePath);

    protected override Task<IGitProjectRepository> CreateRepositoryAsync() =>
        Task.FromResult<IGitProjectRepository>(new SqliteGitProjectRepository(_databasePath));

    [Fact]
    public async Task Contract_Crud_SupportsBoundAndUnboundProjects() =>
        await Crud_SupportsBoundAndUnboundProjectsAsync();

    [Fact]
    public async Task Save_QueuesImmutableProjectSnapshots()
    {
        await InitializeAsync();
        var repository = new SqliteGitProjectRepository(_databasePath);
        var id = Guid.NewGuid();
        var original = new GitProject(id, "原项目", null, null, null);
        await repository.AddAsync(original);
        var updated = new GitProject(id, "新项目", null, null, null);
        Assert.True(await repository.UpdateAsync(updated));

        string[] payloads;
        await using (var connection = SqliteDatabase.Open(_databasePath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT changes_json FROM sync_operation";
            await using var reader = await command.ExecuteReaderAsync();
            var results = new List<string>();
            while (await reader.ReadAsync())
            {
                results.Add(reader.GetString(0));
            }

            payloads = results.ToArray();
        }

        var changes = payloads
            .Select(payload => Assert.Single(JsonSerializer.Deserialize<LocalOperationChange[]>(payload)!))
            .ToArray();
        var add = Assert.Single(changes, change => change.Change == LocalObjectChange.Add);
        var update = Assert.Single(changes, change => change.Change == LocalObjectChange.Update);
        Assert.Equal("原项目", JsonSerializer.Deserialize<GitProject>(add.AfterJson!)!.Name);
        Assert.Equal("原项目", JsonSerializer.Deserialize<GitProject>(update.BeforeJson!)!.Name);
        Assert.Equal("新项目", JsonSerializer.Deserialize<GitProject>(update.AfterJson!)!.Name);
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
