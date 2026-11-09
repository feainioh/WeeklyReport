// [新增-离线存储] Phase 1 切片 D：本地操作队列测试（回调式会话）——业务写入与入队同事务：
// 提交后同存、回调失败同灭、入队失败业务同灭、依赖链与哈希稳定、嵌套拒绝。
// [新增-SQLite单机] 计划《SQLite单机存储改造计划》阶段 2 起：outbox 已解耦——业务写入仍原子提交，
// 但不再新增 sync_operation 行（历史断言以 [旧代码-SQLite单机] 注释保留，新断言验证零入队）。
using System.IO;
using System.Text.Json;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

public sealed class SqliteOperationQueueTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"opqueue-{Guid.NewGuid():N}.db");

    private SqliteOperationQueue Queue => new(_databasePath);

    private SqliteWorkEntryRepository Repository => new(_databasePath);

    private static Task WriteEntryAsync(LocalOperationSession session, WorkEntry entry)
    {
        // 模拟组合层业务写入（与 SqliteWorkEntryRepository 同口径的参数化 INSERT）
        return session.Database.Ado.ExecuteCommandAsync(
            "INSERT INTO work_entries (id, work_date, content, created_at_utc, updated_at_utc, confirmed_minutes) " +
            "VALUES (@id, @workDate, @content, @createdAt, @updatedAt, @confirmedMinutes)",
            new
            {
                id = entry.Id.ToString("D"),
                workDate = entry.WorkDate.ToString("yyyy-MM-dd"),
                content = entry.Content,
                createdAt = entry.CreatedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'"),
                updatedAt = entry.UpdatedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'"),
                confirmedMinutes = entry.ConfirmedMinutes
            });
    }

    private static WorkEntry NewEntry(string content) => new(
        Guid.NewGuid(), new DateOnly(2026, 11, 3), content,
        StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow),
        StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow), 30);

    [Fact]
    public async Task Commit_PersistsBusinessWriteAndQueueRowTogether()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);
        var repository = Repository;
        var entry = NewEntry("操作组内写入");

        var returned = await Queue.RunInOperationAsync("work-entry.save", async session =>
        {
            await WriteEntryAsync(session, entry);
            session.Record(new LocalOperationChange(
                "work_entries", entry.Id.ToString("D"), entry.WorkDate.ToString("yyyy-MM-dd"), LocalObjectChange.Add));
            return "done";
        });

        Assert.Equal("done", returned);
        Assert.NotNull(await repository.FindAsync(entry.Id)); // 业务写入已提交

        // [旧代码-SQLite单机] 原断言：入队行与业务写入同事务提交（outbox 尚未解耦）
        // var row = await ReadSingleOperationRowAsync();
        // Assert.NotNull(row);
        // Assert.Equal("work-entry.save", row!.OperationKind);
        // Assert.Equal("Pending", row.Status);
        // Assert.False(string.IsNullOrEmpty(row.ContentHash));
        // var changes = JsonSerializer.Deserialize<LocalOperationChange[]>(row.ChangesJson);
        // var change = Assert.Single(changes!);
        // Assert.Equal("work_entries", change.Table);
        // Assert.Equal(LocalObjectChange.Add, change.Change);

        // [新增-SQLite单机] 计划阶段 2：outbox 解耦后 Record 变更不再入队——业务提交成功但 sync_operation 零新增
        Assert.Null(await ReadSingleOperationRowAsync());
    }

    [Fact]
    public async Task NoChanges_CommitsWithoutCreatingAnOperation()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);

        var result = await Queue.RunInOperationAsync("work-entry.save", _ => Task.FromResult(false));

        Assert.False(result);
        Assert.Empty(await ReadAllOperationRowsAsync());
    }

    [Fact]
    public async Task BusinessFailure_RollsBackBusinessAndQueue()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);
        var repository = Repository;
        var entry = NewEntry("将回滚的写入");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Queue.RunInOperationAsync<object?>("work-entry.save", async session =>
            {
                await WriteEntryAsync(session, entry);
                session.Record(new LocalOperationChange(
                    "work_entries", entry.Id.ToString("D"), "rollback", LocalObjectChange.Add));
                throw new InvalidOperationException("业务失败");
            }));

        Assert.Null(await repository.FindAsync(entry.Id));
        Assert.Null(await ReadSingleOperationRowAsync());
    }

    [Fact]
    public async Task EnqueueFailure_RollsBackBusiness()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);
        // 删除队列表模拟入队失败（EnsureInitializedAsync 的日记已记录该脚本，不会重建）
        await using (var connection = SqliteDatabase.Open(_databasePath))
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE sync_operation";
            await drop.ExecuteNonQueryAsync();
        }

        // [旧代码-SQLite单机] 原断言：入队失败 → 业务写入一并回滚（outbox 与业务同生共死）
        // var repository = Repository;
        // var entry = NewEntry("入队失败回滚");
        // var exception = await Assert.ThrowsAsync<DatabaseAccessException>(() =>
        //     Queue.RunInOperationAsync<object?>("work-entry.save", async session =>
        //     {
        //         await WriteEntryAsync(session, entry);
        //         session.Record(new LocalOperationChange(
        //             "work_entries", entry.Id.ToString("D"), "2026-11-03", LocalObjectChange.Add));
        //         return null;
        //     }));
        //
        // Assert.Contains("本地存储操作失败", exception.Message);
        // Assert.Null(await repository.FindAsync(entry.Id)); // 入队失败 → 业务写入一并回滚

        // [新增-SQLite单机] 计划阶段 2：outbox 解耦后业务事务不再触碰 sync_operation——
        // 队列表缺失不影响业务写入提交（解耦的直接证据）
        var repository = Repository;
        var entry = NewEntry("无队列表业务提交");
        await Queue.RunInOperationAsync<object?>("work-entry.save", async session =>
        {
            await WriteEntryAsync(session, entry);
            session.Record(new LocalOperationChange(
                "work_entries", entry.Id.ToString("D"), "2026-11-03", LocalObjectChange.Add));
            return null;
        });

        Assert.NotNull(await repository.FindAsync(entry.Id)); // 业务写入已提交
    }

    [Fact]
    public async Task OperationConnection_EnforcesDeclaredForeignKeys()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);

        var exception = await Assert.ThrowsAsync<DatabaseAccessException>(() =>
            Queue.RunInOperationAsync("git-work-item.save", async session =>
            {
                await session.Database.Ado.ExecuteCommandAsync(
                    "INSERT INTO git_work_items " +
                    "(id, project_id, work_date, description, estimated_minutes, confirmed_minutes, is_deleted, revision) " +
                    "VALUES (@id, @projectId, @date, @description, NULL, NULL, 0, 0)",
                    new
                    {
                        id = Guid.NewGuid().ToString("D"),
                        projectId = Guid.NewGuid().ToString("D"),
                        date = "2026-11-03",
                        description = "孤立工作项"
                    });
                session.Record(new LocalOperationChange(
                    "git_work_items", "orphan", "2026-11-03", LocalObjectChange.Add));
                return true;
            }));

        Assert.Contains("本地存储操作失败", exception.Message);
        await using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM git_work_items";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task SequentialOperations_FormDependsOnChainWithStableHash()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);
        var repository = Repository;
        var firstEntry = NewEntry("第一组");
        var secondEntry = NewEntry("第二组");
        var first = await Queue.RunInOperationAsync("work-entry.save", async session =>
        {
            await WriteEntryAsync(session, firstEntry);
            session.Record(new LocalOperationChange("work_entries", "a", "2026-11-03", LocalObjectChange.Add));
            return session.GroupId;
        });
        var second = await Queue.RunInOperationAsync("work-entry.save", async session =>
        {
            await WriteEntryAsync(session, secondEntry);
            session.Record(new LocalOperationChange("work_entries", "a", "2026-11-03", LocalObjectChange.Add)); // 相同变更集合
            return session.GroupId;
        }, dependsOn: first);

        // [旧代码-SQLite单机] 原断言：两组各留一行且 depends_on 成链、内容哈希一致（同步幂等回执基础）
        // var rows = await ReadAllOperationRowsAsync();
        // Assert.Equal(2, rows.Count);
        // Assert.Equal(first.ToString("D"), rows.Single(row => row.OpGroupId == second.ToString("D")).DependsOn);
        // Assert.Equal(
        //     rows.Single(row => row.OpGroupId == first.ToString("D")).ContentHash,
        //     rows.Single(row => row.OpGroupId == second.ToString("D")).ContentHash); // 幂等回执基础

        // [新增-SQLite单机] 计划阶段 2：顺序操作各自独立提交（组 ID 仍生成、依赖参数仍接受），但零入队
        Assert.NotEqual(first, second);
        Assert.Empty(await ReadAllOperationRowsAsync());
        Assert.NotNull(await repository.FindAsync(firstEntry.Id));
        Assert.NotNull(await repository.FindAsync(secondEntry.Id));
    }

    [Fact]
    public async Task NestedRun_IsRejectedAndOuterCommits()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);
        var nestedRejected = false;

        var entry = NewEntry("嵌套防护");
        await Queue.RunInOperationAsync("work-entry.save", async session =>
        {
            await WriteEntryAsync(session, entry);
            try
            {
                await Queue.RunInOperationAsync<object?>("git-sync.apply", _ => Task.FromResult<object?>(null));
            }
            catch (InvalidOperationException)
            {
                nestedRejected = true;
            }

            session.Record(new LocalOperationChange("work_entries", entry.Id.ToString("D"), "2026-11-03", LocalObjectChange.Add));
            return true;
        });

        Assert.True(nestedRejected);
        Assert.NotNull(await Repository.FindAsync(entry.Id));
    }

    private async Task<OperationRow?> ReadSingleOperationRowAsync()
    {
        var rows = await ReadAllOperationRowsAsync();
        return rows.FirstOrDefault();
    }

    private async Task<List<OperationRow>> ReadAllOperationRowsAsync()
    {
        await using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT op_group_id, operation_kind, changes_json, depends_on, status, content_hash, " +
            "attempted_at_utc FROM sync_operation ORDER BY created_at_utc, op_group_id";
        var rows = new List<OperationRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new OperationRow(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return rows;
    }

    private sealed record OperationRow(
        string OpGroupId, string OperationKind, string ChangesJson, string? DependsOn,
        string Status, string ContentHash, string? AttemptedAtUtc);

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
