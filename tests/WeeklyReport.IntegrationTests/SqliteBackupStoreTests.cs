// [新增-备份离线化] 切片 2：SQLite 备份仓储契约测试——捕获一致性/导入确认门禁/覆盖跳过计数/
// 基线变化拒绝/失败整体回滚/字段完整往返，与 MySQL 共用契约基类。
using System.IO;
using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Persistence;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

public sealed class SqliteBackupStoreTests : BackupStoreContract, IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"backup-{Guid.NewGuid():N}.db");

    protected override async Task<BackupService> CreateServiceAsync()
    {
        await SqliteDatabase.EnsureInitializedAsync(_databasePath);
        return new BackupService(new SqliteBackupStore(_databasePath), new BackupImportPlanner(), TimeProvider.System);
    }

    protected override Task TouchManualEntryAsync(Guid id, string newContent)
    {
        return new SqliteOperationQueue(_databasePath).RunInOperationAsync("work-entry.save", async session =>
        {
            // [新增-备份离线化] SqlSugar 不支持 ValueTuple 映射，改分步单值查询
            var workDates = await session.Database.Ado.SqlQueryAsync<string>(
                "SELECT work_date FROM work_entries WHERE id = @id", new { id = id.ToString("D") });
            Assert.True(workDates.Count > 0, "工作记录应存在");
            await session.Database.Ado.ExecuteCommandAsync(
                "UPDATE work_entries SET content = @content WHERE id = @id",
                new { id = id.ToString("D"), content = newContent });
            session.Record(new LocalOperationChange(
                "work_entries", id.ToString("D"), workDates[0], LocalObjectChange.Update));
            return true;
        });
    }

    protected override async Task InjectReportInsertFailureAsync(Guid reportId)
    {
        await using var connection = SqliteDatabase.Open(_databasePath);
        var trigger = connection.CreateCommand();
        trigger.CommandText =
            "CREATE TRIGGER IF NOT EXISTS spike_backup_fail BEFORE INSERT ON weekly_reports FOR EACH ROW " +
            $"WHEN NEW.id = '{reportId:D}' BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END";
        await trigger.ExecuteNonQueryAsync();
    }

    protected override async Task RemoveReportInsertFailureAsync()
    {
        await using var connection = SqliteDatabase.Open(_databasePath);
        var trigger = connection.CreateCommand();
        trigger.CommandText = "DROP TRIGGER IF EXISTS spike_backup_fail";
        await trigger.ExecuteNonQueryAsync();
    }

    protected override async Task CleanupAsync(Guid project, Guid manual, Guid report)
    {
        await using var connection = SqliteDatabase.Open(_databasePath);
        foreach (var statement in new[]
        {
            "DELETE FROM git_commit_links WHERE project_id = @project",
            "DELETE FROM git_work_items WHERE project_id = @project",
            "DELETE FROM git_projects WHERE id = @project",
            "DELETE FROM work_entries WHERE id = @manual",
            "DELETE FROM weekly_reports WHERE id = @report"
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.Parameters.AddWithValue("@project", project.ToString("D"));
            command.Parameters.AddWithValue("@manual", manual.ToString("D"));
            command.Parameters.AddWithValue("@report", report.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Contract_Import_SkipOverwriteAndStalePreview() =>
        await Import_SkipOverwriteAndStalePreviewAsync();

    [Fact]
    public async Task Contract_Import_FailureMidwayRollsBack() =>
        await Import_FailureMidwayRollsBackAsync();

    [Fact]
    public async Task Contract_Capture_IsStableAcrossCalls() =>
        await Capture_IsStableAcrossCallsAsync();

    [Fact]
    public async Task Import_AutoEnqueuesAsSingleOperation()
    {
        var service = await CreateServiceAsync();
        var source = CreateBackup(FindUnusedWeek(await service.CaptureAsync()));
        var projectId = source.Projects[0].Id;
        var manualId = source.Entries.Single(e => e.Kind == BackupEntryKind.Manual).Id;
        var reportId = source.Reports[0].Id;

        try
        {
            var plan = await service.PrepareImportAsync(source);
            await service.ApplyImportAsync(plan, BackupConflictPolicy.Skip, true);

            // 验证导入自动入队为一个操作组
            await using var connection = SqliteDatabase.Open(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), operation_kind FROM sync_operation GROUP BY operation_kind";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt64(0));
            Assert.Equal("backup.import", reader.GetString(1));
        }
        finally
        {
            await CleanupAsync(projectId, manualId, reportId);
        }
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
