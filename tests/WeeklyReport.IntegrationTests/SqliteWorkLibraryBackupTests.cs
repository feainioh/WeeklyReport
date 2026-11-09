// [新增-SQLite单机] 《SQLite单机存储改造计划》阶段 4：完整工作库备份恢复测试——
// WAL 写入期间快照一致性、备份→变更→恢复往返、恢复前验证拒绝坏备份/非工作库、失败自动回滚保留原库。
using System.IO;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

public sealed class SqliteWorkLibraryBackupTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"backup-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(_directory, "work.db");

    private string BackupPath => Path.Combine(_directory, "work-backup.db");

    private SqliteWorkLibraryBackup Backup => new();

    public SqliteWorkLibraryBackupTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task CreateBackup_DuringWalWrites_ProducesConsistentSnapshot()
    {
        await SqliteDatabase.EnsureInitializedAsync(DatabasePath);
        var repository = new SqliteWorkEntryRepository(DatabasePath);
        var first = NewEntry("备份前写入");
        await repository.AddAsync(first);
        SqliteDatabase.EnableWal(DatabasePath); // 确保库处于 WAL 模式

        await using var reader = SqliteDatabase.Open(DatabasePath);
        await using var readTransaction = reader.BeginTransaction(deferred: true);
        using var read = reader.CreateCommand();
        read.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)readTransaction;
        read.CommandText = "SELECT COUNT(*) FROM work_entries";
        await read.ExecuteScalarAsync();

        var second = NewEntry("备份时写入");
        await repository.AddAsync(second);

        await Backup.CreateBackupAsync(DatabasePath, BackupPath);

        // 备份内容完整可读：两条业务数据都在，且 quick_check 已在备份内通过
        var restored = new SqliteWorkEntryRepository(BackupPath);
        Assert.NotNull(await restored.FindAsync(first.Id));
        Assert.NotNull(await restored.FindAsync(second.Id));
    }

    [Fact]
    public async Task CreateBackup_SamePathAndExistingBackup_PreserveBothFiles()
    {
        await SqliteDatabase.EnsureInitializedAsync(DatabasePath);
        var entry = NewEntry("原始数据");
        await new SqliteWorkEntryRepository(DatabasePath).AddAsync(entry);
        await Backup.CreateBackupAsync(DatabasePath, BackupPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Backup.CreateBackupAsync(DatabasePath, DatabasePath));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Backup.CreateBackupAsync(DatabasePath, BackupPath));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Backup.CreateBackupAsync(DatabasePath, Path.Combine(_directory, "other.db-wal")));

        Assert.NotNull(await new SqliteWorkEntryRepository(DatabasePath).FindAsync(entry.Id));
        Assert.NotNull(await new SqliteWorkEntryRepository(BackupPath).FindAsync(entry.Id));
    }

    [Fact]
    public async Task Restore_WhenDatabaseIsOpen_RejectsAndPreservesCurrentWalData()
    {
        await SqliteDatabase.EnsureInitializedAsync(DatabasePath);
        var repository = new SqliteWorkEntryRepository(DatabasePath);
        var original = NewEntry("快照");
        await repository.AddAsync(original);
        await Backup.CreateBackupAsync(DatabasePath, BackupPath);

        await using var reader = SqliteDatabase.Open(DatabasePath);
        await using var readTransaction = reader.BeginTransaction(deferred: true);
        using var read = reader.CreateCommand();
        read.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)readTransaction;
        read.CommandText = "SELECT COUNT(*) FROM work_entries";
        await read.ExecuteScalarAsync();
        var latest = NewEntry("WAL 新数据");
        await repository.AddAsync(latest);

        await Assert.ThrowsAnyAsync<IOException>(() => Backup.RestoreAsync(BackupPath, DatabasePath));
        Assert.NotNull(await repository.FindAsync(latest.Id));
        var preRestore = Assert.Single(Directory.GetFiles(_directory, "work.db.pre-restore-*.bak"));
        Assert.NotNull(await new SqliteWorkEntryRepository(preRestore).FindAsync(latest.Id));
    }

    [Fact]
    public async Task Restore_CancelledBeforeReplacement_PreservesCurrentData()
    {
        await SqliteDatabase.EnsureInitializedAsync(DatabasePath);
        await Backup.CreateBackupAsync(DatabasePath, BackupPath);
        var entry = NewEntry("保留");
        var repository = new SqliteWorkEntryRepository(DatabasePath);
        await repository.AddAsync(entry);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Backup.RestoreAsync(BackupPath, DatabasePath, cancellation.Token));
        Assert.NotNull(await repository.FindAsync(entry.Id));
    }

    [Fact]
    public async Task Restore_RoundTripsToBackupStateAndKeepsPreRestoreCopy()
    {
        await SqliteDatabase.EnsureInitializedAsync(DatabasePath);
        var repository = new SqliteWorkEntryRepository(DatabasePath);
        var original = NewEntry("备份中的记录");
        await repository.AddAsync(original);
        await Backup.CreateBackupAsync(DatabasePath, BackupPath);

        // 备份后继续写入（恢复应丢弃这部分）
        var afterBackup = NewEntry("备份后的记录");
        await repository.AddAsync(afterBackup);

        var preRestorePath = await Backup.RestoreAsync(BackupPath, DatabasePath);

        Assert.True(File.Exists(preRestorePath), "恢复前的原库副本必须保留");
        var restored = new SqliteWorkEntryRepository(DatabasePath);
        Assert.NotNull(await restored.FindAsync(original.Id));
        Assert.Null(await restored.FindAsync(afterBackup.Id)); // 恢复后回到备份时点
        Assert.NotNull(await new SqliteWorkEntryRepository(preRestorePath).FindAsync(afterBackup.Id)); // 副本含恢复前数据
    }

    [Fact]
    public async Task CreateBackup_OnGarbageFile_FailsWithClearError()
    {
        File.WriteAllBytes(DatabasePath, "this is not a sqlite database"u8.ToArray());

        var exception = await Assert.ThrowsAsync<DatabaseAccessException>(
            () => Backup.CreateBackupAsync(DatabasePath, BackupPath));

        Assert.Contains("备份失败", exception.Message);
    }

    [Fact]
    public async Task Restore_FromNonWorkLibraryFile_RejectsBeforeTouchingTarget()
    {
        await SqliteDatabase.EnsureInitializedAsync(DatabasePath);
        var repository = new SqliteWorkEntryRepository(DatabasePath);
        var kept = NewEntry("必须保留的记录");
        await repository.AddAsync(kept);

        // 构造一个有效 SQLite 但缺工作库核心表的文件（quick_check 通过、结构校验失败）
        var foreignPath = Path.Combine(_directory, "foreign.db");
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={foreignPath}"))
        {
            await connection.OpenAsync();
            using var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE unrelated (id INTEGER PRIMARY KEY)";
            await create.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<DatabaseAccessException>(
            () => Backup.RestoreAsync(foreignPath, DatabasePath));

        Assert.Contains("工作库结构不完整", exception.Message);
        Assert.NotNull(await new SqliteWorkEntryRepository(DatabasePath).FindAsync(kept.Id)); // 原库未被触碰
    }

    [Fact]
    public async Task Restore_BackupWithoutConflictTable_RejectsBeforeTouchingTarget()
    {
        await SqliteDatabase.EnsureInitializedAsync(DatabasePath);
        var repository = new SqliteWorkEntryRepository(DatabasePath);
        var entry = NewEntry("保留的记录");
        await repository.AddAsync(entry);
        await Backup.CreateBackupAsync(DatabasePath, BackupPath);
        await using (var connection = SqliteDatabase.Open(BackupPath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE sync_conflict";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<DatabaseAccessException>(() => Backup.RestoreAsync(BackupPath, DatabasePath));
        Assert.NotNull(await repository.FindAsync(entry.Id));
    }

    [Fact]
    public async Task Restore_FromGarbageBackup_RollsBackAndKeepsOriginal()
    {
        await SqliteDatabase.EnsureInitializedAsync(DatabasePath);
        var repository = new SqliteWorkEntryRepository(DatabasePath);
        var kept = NewEntry("回滚后仍在的记录");
        await repository.AddAsync(kept);

        var garbagePath = Path.Combine(_directory, "garbage.db");
        File.WriteAllBytes(garbagePath, "garbage bytes"u8.ToArray());

        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => Backup.RestoreAsync(garbagePath, DatabasePath));

        // 坏备份在恢复前验证即被拒绝（验证阶段报错），原库必须完好
        Assert.NotNull(await new SqliteWorkEntryRepository(DatabasePath).FindAsync(kept.Id));
        Assert.Contains("工作库", exception.Message);
    }

    private static WorkEntry NewEntry(string content) => new(
        Guid.NewGuid(),
        new DateOnly(2026, 11, 9),
        content,
        StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow),
        StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow),
        30);

    public void Dispose()
    {
        // [新增-离线存储] 清连接池后删除临时库目录（db/-wal/-shm/备份副本一并清理）
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var main = DatabasePath + suffix;
            if (File.Exists(main))
            {
                File.Delete(main);
            }

            var backup = BackupPath + suffix;
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
