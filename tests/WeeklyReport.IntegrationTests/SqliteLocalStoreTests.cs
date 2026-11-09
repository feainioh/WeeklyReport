// [新增-离线存储] Phase 1 切片 E：本地存储状态与镜像测试——未初始化状态、空工作区（无已验证基线）、
// 完整镜像（数据保真 + Ready + 已验证基线）、队列未清空拒绝覆盖、损坏文件明确报错、待同步计数。
using System.IO;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

public sealed class SqliteLocalStoreTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"localstore-{Guid.NewGuid():N}.db");

    private SqliteLocalStore Store => new(_databasePath);

    [Fact]
    public async Task GetState_FreshDatabase_IsNotInitializedWithZeroCounts()
    {
        var state = await Store.GetStateAsync();

        Assert.Equal(LocalStorageStatus.NotInitialized, state.Status);
        Assert.False(state.HasVerifiedBaseline);
        Assert.Equal(0, state.PendingCount);
        Assert.Equal(0, state.ConflictCount);
        Assert.Null(state.LastMirrorAtUtc);
    }

    [Fact]
    public async Task StartEmptyWorkspace_IsReadyWithoutVerifiedBaselineAndWritable()
    {
        await Store.StartEmptyWorkspaceAsync();

        var state = await Store.GetStateAsync();
        Assert.Equal(LocalStorageStatus.Ready, state.Status);
        Assert.False(state.HasVerifiedBaseline); // 无已验证基线（§4.4）

        var repository = new SqliteWorkEntryRepository(_databasePath);
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var entry = new WorkEntry(Guid.NewGuid(), new DateOnly(2026, 11, 4), "空工作区写入", now, now, null);
        await repository.AddAsync(entry);
        Assert.NotNull(await repository.FindAsync(entry.Id)); // 空工作区可继续录入
        await repository.DeleteAsync(entry.Id);
    }

    /// <summary>确认暂停原因在本地数据库中持久化，并只在显式恢复后清除。</summary>
    [Fact]
    public async Task SuspendSync_PersistsReasonUntilExplicitlyResumed()
    {
        await Store.StartEmptyWorkspaceAsync();

        await Store.SuspendSyncAsync("MySQL 身份不匹配");

        var suspended = await Store.GetStateAsync();
        Assert.Equal(LocalStorageStatus.Suspended, suspended.Status);
        Assert.Equal("MySQL 身份不匹配", suspended.StatusReason);

        await Store.ResumeSyncAsync();

        var resumed = await Store.GetStateAsync();
        Assert.Equal(LocalStorageStatus.Ready, resumed.Status);
        Assert.Null(resumed.StatusReason);
    }

    [Fact]
    public async Task InitializeMirror_MarksReadyVerifiedAndPreservesFullFidelity()
    {
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var projectId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var week = new WeekPeriod(new DateOnly(2026, 11, 2));
        var project = new GitProject(projectId, "镜像项目", @"C:\repo\demo", GitAuthorField.Email, "dev@example.invalid");
        var authoredAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        var item = new GitWorkItemSnapshot(
            itemId, projectId, week.Start, "已删除的工作项", null, true,
            [new GitCommit(projectId, new string('c', 40), "作者", "a@example.invalid", authoredAt, "摘要")],
            null, 7);
        var entry = new WorkEntry(entryId, week.Start, "镜像手工记录 😀", now, now, 75);
        var report = new WeeklyReportSnapshot(
            Guid.NewGuid(), week, "本周", "计划", "风险", 90, 45, 1, now, now);

        await Store.InitializeMirrorAsync([project], [item], [entry], [report], Guid.NewGuid());

        var state = await Store.GetStateAsync();
        Assert.Equal(LocalStorageStatus.Ready, state.Status);
        Assert.True(state.HasVerifiedBaseline);
        Assert.NotNull(state.LastMirrorAtUtc);

        // 数据保真：绑定路径/作者、软删除与修订号、提交链接、工时数值、周报快照全部往返
        var projects = new SqliteGitProjectRepository(_databasePath);
        var loaded = await projects.FindAsync(projectId);
        Assert.NotNull(loaded);
        Assert.Equal(project.RepositoryPath, loaded!.RepositoryPath);
        Assert.Equal(project.Author, loaded.Author);

        var mirrored = (await new SqliteGitSyncStore(_databasePath).LoadAllAsync()).Single();
        Assert.Equal(itemId, mirrored.Id);
        Assert.True(mirrored.IsDeleted);
        Assert.Equal(7, mirrored.Revision);
        Assert.Single(mirrored.Commits);

        var entries = await new SqliteWorkEntryRepository(_databasePath).ListAllAsync();
        var mirroredEntry = Assert.Single(entries);
        Assert.Equal(entry.Content, mirroredEntry.Content);
        Assert.Equal(75, mirroredEntry.ConfirmedMinutes);
        Assert.Equal(now, mirroredEntry.CreatedAtUtc);

        var mirroredReport = await new SqliteWeeklyReportRepository(_databasePath).FindByWeekAsync(week);
        Assert.NotNull(mirroredReport);
        Assert.Equal(90, mirroredReport!.ConfirmedMinutes);
        Assert.Equal(1, mirroredReport.UnknownHoursCount);
    }

    [Fact]
    public async Task InitializeMirror_IsRejectedWhenQueueHasPendingOperations()
    {
        // [新增-SQLite单机] 覆盖保留镜像防护语义：按路径注册入队以播种待同步操作（生产默认不入队）
        SqliteOperationQueue.SetEnqueueEnabled(_databasePath, true);
        var queue = new SqliteOperationQueue(_databasePath);
        var entryId = Guid.NewGuid();
        await queue.RunInOperationAsync("work-entry.save", async session =>
        {
            await session.Database.Ado.ExecuteCommandAsync(
                "INSERT INTO work_entries (id, work_date, content, created_at_utc, updated_at_utc, confirmed_minutes) " +
                "VALUES (@id, @date, @content, @created, @updated, NULL)",
                new
                {
                    id = entryId.ToString("D"),
                    date = "2026-11-04",
                    content = "待同步写入",
                    created = "2026-11-04T00:00:00.000000Z",
                    updated = "2026-11-04T00:00:00.000000Z"
                });
            session.Record(new LocalOperationChange(
                "work_entries", entryId.ToString("D"), "2026-11-04", LocalObjectChange.Add));
            return true;
        });
        var stateAfterQueue = await Store.GetStateAsync();
        Assert.Equal(1, stateAfterQueue.PendingCount);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Store.InitializeMirrorAsync([], [], [], [], Guid.NewGuid()));

        var state = await Store.GetStateAsync();
        Assert.Equal(LocalStorageStatus.NotInitialized, state.Status); // 未标记镜像
        Assert.Equal(1, state.PendingCount); // 队列保留，未静默丢弃
    }

    [Fact]
    public async Task CorruptDatabaseFile_ThrowsDatabaseAccessException()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await File.WriteAllTextAsync(_databasePath, "这不是 SQLite 数据库文件");
        await Assert.ThrowsAsync<DatabaseAccessException>(() => Store.GetStateAsync());
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
