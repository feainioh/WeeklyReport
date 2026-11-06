// [新增-离线存储] Phase 1 切片 E：MySQL → SQLite 真实首次镜像链路——从 MySQL 仓储读取（含本机绑定），
// 经 SqliteLocalStore 单事务写入，SQLite 仓储按同契约读回核对（计划 §4.4 首次在线初始化）。
using System.Data;
using System.IO;
using MySqlConnector;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence;
using WeeklyReport.Infrastructure.Persistence.Sqlite;

namespace WeeklyReport.IntegrationTests;

[Collection("MySqlIntegration")]
[Trait("Category", "MySql")]
public class MySqlLocalMirrorTests
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "WeeklyReport.SqliteTests", $"mirror-{Guid.NewGuid():N}.db");

    [MySqlIntegrationFact]
    public async Task FirstMirror_FromMysqlRepositories_PreservesBindingsAndBusinessData()
    {
        var settings = MySqlIntegrationSupport.LoadSettings();
        await new MySqlDatabaseInitializer().InitializeAsync(settings);
        var configuration = new MySqlIntegrationSupport.FixedConfigurationStore(settings);
        var projects = new SqlSugarGitProjectRepository(configuration);
        var entries = new SqlSugarWorkEntryRepository(configuration);
        var reports = new SqlSugarWeeklyReportRepository(configuration);

        var projectId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var week = WeekPeriod.FromDate(DateOnly.FromDateTime(DateTime.Today));
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var seeded = false;

        try
        {
            await projects.AddAsync(new GitProject(projectId, "镜像源项目", Path.GetTempPath(), GitAuthorField.Email, "mirror@example.invalid"));
            await entries.AddAsync(new WorkEntry(entryId, week.Start, "MySQL 源记录", now, now, 60));
            await reports.AddAsync(new WeeklyReportSnapshot(Guid.NewGuid(), week, "源周报", "计划", "风险", 60, 0, 0, now, now));
            seeded = true;

            var snapshot = await new MySqlLocalMirrorSnapshotReader().ReadAsync(settings);
            var store = new SqliteLocalStore(_databasePath);
            await store.InitializeMirrorAsync(
                snapshot.Projects,
                snapshot.WorkItems,
                snapshot.WorkEntries,
                snapshot.Reports,
                mirrorIdentity: snapshot.Identity.DatabaseId);

            var state = await store.GetStateAsync();
            Assert.Equal(LocalStorageStatus.Ready, state.Status);
            Assert.True(state.HasVerifiedBaseline);

            var mirroredProjects = new SqliteGitProjectRepository(_databasePath);
            var bound = await mirroredProjects.FindAsync(projectId);
            Assert.NotNull(bound);
            Assert.True(bound!.IsBound); // 本机绑定随镜像保留（BackupDocument 不承载的能力）
            Assert.Equal("mirror@example.invalid", bound.Author);

            var mirroredEntry = await new SqliteWorkEntryRepository(_databasePath).FindAsync(entryId);
            Assert.NotNull(mirroredEntry);
            Assert.Equal("MySQL 源记录", mirroredEntry!.Content);
            Assert.Equal(60, mirroredEntry.ConfirmedMinutes);

            var mirroredReport = await new SqliteWeeklyReportRepository(_databasePath).FindByWeekAsync(week);
            Assert.NotNull(mirroredReport);
            Assert.Equal("源周报", mirroredReport!.WorkSummary);
        }
        finally
        {
            if (seeded)
            {
                await entries.DeleteAsync(entryId);
                await reports.DeleteAsync(
                    (await reports.FindByWeekAsync(week))!.Id,
                    (await reports.FindByWeekAsync(week))!.UpdatedAtUtc);
                await projects.DeleteAsync(projectId);
            }

            CleanupSqlite();
        }
    }

    [MySqlIntegrationFact]
    public async Task ReadOnlyRepeatableRead_KeepsMirrorSourceStableDuringConcurrentUpdate()
    {
        var settings = MySqlIntegrationSupport.LoadSettings();
        await new MySqlDatabaseInitializer().InitializeAsync(settings);
        var repository = new SqlSugarWorkEntryRepository(
            new MySqlIntegrationSupport.FixedConfigurationStore(settings));
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var entry = new WorkEntry(
            Guid.NewGuid(), new DateOnly(2026, 11, 2), "snapshot-before", now, now);
        var inserted = false;

        try
        {
            await repository.AddAsync(entry);
            inserted = true;
            await using var connection = MySqlIntegrationSupport.CreateConnection(settings);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, isReadOnly: true);

            var before = await ReadContentAsync(connection, transaction, entry.Id);
            var updated = new WorkEntry(
                entry.Id, entry.WorkDate, "snapshot-after", entry.CreatedAtUtc, now.AddMinutes(1));
            Assert.True(await repository.UpdateAsync(updated));
            var during = await ReadContentAsync(connection, transaction, entry.Id);
            await transaction.CommitAsync();

            Assert.Equal("snapshot-before", before);
            Assert.Equal(before, during);
            Assert.Equal("snapshot-after", (await repository.FindAsync(entry.Id))!.Content);
        }
        finally
        {
            if (inserted)
            {
                await repository.DeleteAsync(entry.Id);
            }
        }
    }

    private static async Task<string> ReadContentAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT content FROM work_entries WHERE id = @id";
        command.Parameters.AddWithValue("@id", id.ToString("D"));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private void CleanupSqlite()
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
