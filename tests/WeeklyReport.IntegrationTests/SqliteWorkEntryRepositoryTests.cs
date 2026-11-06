// [新增-离线存储] Phase 1 切片 A：SQLite 工作记录仓储契约测试——临时库文件直跑（无环境门禁），
// 验证迁移幂等、CRUD、周边界与微秒精度在 SQLite 侧与 MySQL 行为一致（ADR-02/05）。
using System.IO;
using System.Text.Json;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

public sealed class SqliteWorkEntryRepositoryTests : WorkEntryRepositoryContract, IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"workentries-{Guid.NewGuid():N}.db");

    protected override async Task InitializeAsync()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);
    }

    protected override Task<IWorkEntryRepository> CreateRepositoryAsync() =>
        Task.FromResult<IWorkEntryRepository>(new SqliteWorkEntryRepository(_databasePath));

    [Fact]
    public async Task Contract_MigrationAndCrud() => await MigrationAndCrudAsync();

    [Fact]
    public async Task Add_NormalizesOffsetAndPreservesMicroseconds()
    {
        await InitializeAsync();
        var repository = new SqliteWorkEntryRepository(_databasePath);
        var createdAt = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.FromMinutes(330)).AddTicks(1230);
        var entry = new WorkEntry(Guid.NewGuid(), new DateOnly(2026, 11, 4), "时间精度", createdAt, createdAt, null);

        await repository.AddAsync(entry);
        var loaded = await repository.FindAsync(entry.Id);

        Assert.NotNull(loaded);
        Assert.Equal(TimeSpan.Zero, loaded!.CreatedAtUtc.Offset);
        Assert.Equal(createdAt.ToUniversalTime(), loaded.CreatedAtUtc);
        Assert.Equal(createdAt.ToUniversalTime(), loaded.UpdatedAtUtc);
    }

    [Fact]
    public async Task Add_QueuedSnapshotIsUnchangedByLaterLocalEdit()
    {
        await InitializeAsync();
        var repository = new SqliteWorkEntryRepository(_databasePath);
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var entry = new WorkEntry(Guid.NewGuid(), new DateOnly(2026, 11, 4), "原始载荷", now, now, 30);

        await repository.AddAsync(entry);
        await using (var connection = SqliteDatabase.Open(_databasePath))
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE work_entries SET content = @content WHERE id = @id";
            update.Parameters.AddWithValue("@content", "后续本地修改");
            update.Parameters.AddWithValue("@id", entry.Id.ToString("D"));
            await update.ExecuteNonQueryAsync();
        }

        string changesJson;
        int payloadVersion;
        await using (var queueConnection = SqliteDatabase.Open(_databasePath))
        {
            using var query = queueConnection.CreateCommand();
            query.CommandText = "SELECT changes_json, payload_version FROM sync_operation";
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            changesJson = reader.GetString(0);
            payloadVersion = reader.GetInt32(1);
        }

        var changes = JsonSerializer.Deserialize<LocalOperationChange[]>(changesJson);
        var change = Assert.Single(changes!);
        var snapshot = JsonSerializer.Deserialize<WorkEntry>(change.AfterJson!);

        Assert.Equal(2, payloadVersion);
        Assert.Equal("原始载荷", snapshot!.Content);
        Assert.Equal("后续本地修改", (await repository.FindAsync(entry.Id))!.Content);
    }

    [Fact]
    public async Task Contract_GetByWeek_ExcludesNextWeekStart() => await GetByWeek_ExcludesNextWeekStartAsync();

    // [新增-离线存储] Phase 1 切片 E：全量列举契约包装
    [Fact]
    public async Task Contract_ListAll_ReturnsEveryEntryOrdered() => await ListAll_ReturnsEveryEntryOrderedAsync();

    [Fact]
    public async Task Migration_JournalRecordsAppliedScriptsIdempotently()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);

        await using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schemalite_version";
        var count = (long)command.ExecuteScalar()!;
        Assert.Equal(7, count);
    }

    public void Dispose()
    {
        // [新增-离线存储] SqlSugar 自动关闭的连接由 Microsoft.Data.Sqlite 连接池持有，清池后才能删除临时库文件
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = _databasePath + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        if (Directory.Exists(Path.GetDirectoryName(_databasePath))
            && !Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(_databasePath)!).Any())
        {
            Directory.Delete(Path.GetDirectoryName(_databasePath)!);
        }
    }
}
