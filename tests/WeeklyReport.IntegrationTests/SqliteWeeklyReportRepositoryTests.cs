// [新增-离线存储] Phase 1 切片 C：SQLite 周报仓储契约测试——临时库直跑，与 MySQL 同一契约
//（周唯一、乐观并发、快照往返、起止日期校验）。
using System.IO;
using System.Text.Json;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

public sealed class SqliteWeeklyReportRepositoryTests : WeeklyReportRepositoryContract, IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"weeklyreports-{Guid.NewGuid():N}.db");

    protected override Task InitializeAsync() => SqliteDatabase.EnsureInitializedAsync(_databasePath);

    protected override Task<IWeeklyReportRepository> CreateRepositoryAsync() =>
        Task.FromResult<IWeeklyReportRepository>(new SqliteWeeklyReportRepository(_databasePath));

    [Fact]
    public async Task Contract_Crud_PreservesSnapshotAndEnforcesOneReportPerWeek() =>
        await Crud_PreservesSnapshotAndEnforcesOneReportPerWeekAsync();

    [Fact]
    public async Task Save_QueuesImmutableBeforeAndAfterSnapshots()
    {
        await InitializeAsync();
        var repository = new SqliteWeeklyReportRepository(_databasePath);
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var week = new WeekPeriod(new DateOnly(2026, 11, 2));
        var original = new WeeklyReportSnapshot(
            Guid.NewGuid(), week, "原周报", "原计划", "原风险", 60, 45, 0, now, now);
        await repository.AddAsync(original);

        var updated = new WeeklyReportSnapshot(
            original.Id, week, "新周报", "新计划", "新风险", 75, 50, 0, now, now.AddMinutes(1));
        Assert.True(await repository.UpdateAsync(updated, original.UpdatedAtUtc));

        string[] payloads;
        await using (var connection = SqliteDatabase.Open(_databasePath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT changes_json FROM sync_operation ORDER BY created_at_utc, op_group_id";
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
        using var addJson = JsonDocument.Parse(add.AfterJson!);
        using var updateBeforeJson = JsonDocument.Parse(update.BeforeJson!);
        using var updateAfterJson = JsonDocument.Parse(update.AfterJson!);
        Assert.Equal("原周报", addJson.RootElement.GetProperty("WorkSummary").GetString());
        Assert.Equal("原周报", updateBeforeJson.RootElement.GetProperty("WorkSummary").GetString());
        Assert.Equal("新周报", updateAfterJson.RootElement.GetProperty("WorkSummary").GetString());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqliteSyncApplier(_databasePath, new NoDatabaseConfigurationStore()).SyncNowAsync());
        Assert.Contains("尚未配置 MySQL", exception.Message);
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
