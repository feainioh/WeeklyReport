using System.Security.Cryptography;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Backup;
using WeeklyReport.Infrastructure.Persistence;

namespace WeeklyReport.IntegrationTests;

[Collection("MySqlIntegration")]
[Trait("Category", "MySql")]
public class MySqlBackupStoreTests
{
    [MySqlIntegrationFact]
    public async Task Import_SkipOverwriteAndStalePreviewPreserveBindings()
    {
        var settings = MySqlIntegrationSupport.LoadSettings();
        await new MySqlDatabaseInitializer().InitializeAsync(settings);
        var configuration = new MySqlIntegrationSupport.FixedConfigurationStore(settings);
        var store = new SqlSugarBackupStore(configuration);
        var service = new BackupService(store, new BackupImportPlanner(), TimeProvider.System);
        var current = await service.CaptureAsync();
        var source = CreateBackup(FindUnusedWeek(current));
        var projectId = source.Projects[0].Id;
        var manualId = source.Entries.Single(entry => entry.Kind == BackupEntryKind.Manual).Id;
        var reportId = source.Reports[0].Id;

        try
        {
            var first = await service.PrepareImportAsync(source);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ApplyImportAsync(first, BackupConflictPolicy.Skip, false));
            var added = await service.ApplyImportAsync(first, BackupConflictPolicy.Skip, true);
            Assert.Equal(6, added.Added);
            var imported = await service.CaptureAsync();
            Assert.Contains(imported.Projects, project => project.Id == projectId);
            Assert.Contains(imported.Entries, entry => entry.Id == manualId && entry.ConfirmedMinutes == 75);

            var repeated = await service.PrepareImportAsync(source);
            Assert.Equal(1, repeated.Conflicts.Projects);
            Assert.Equal(1, repeated.Conflicts.ManualEntries);
            Assert.Equal(1, repeated.Conflicts.GitWorkItems);
            Assert.Equal(2, repeated.Conflicts.GitCommits);
            Assert.Equal(1, repeated.Conflicts.Reports);
            var skipped = await service.ApplyImportAsync(repeated, BackupConflictPolicy.Skip, true);
            Assert.Equal(6, skipped.Skipped);

            var projects = new SqlSugarGitProjectRepository(configuration);
            var binding = new GitProject(projectId, source.Projects[0].Name,
                Path.GetTempPath(), GitAuthorField.Email, "test@example.invalid");
            Assert.True(await projects.UpdateAsync(binding));
            var changed = source with
            {
                Projects = [source.Projects[0] with { Name = "覆盖后的项目" }],
                Entries = source.Entries.Select(entry => entry.Kind == BackupEntryKind.Manual
                    ? entry with { Description = "覆盖后的手工记录", ConfirmedMinutes = 90 }
                    : entry with { Description = "覆盖后的 Git 工作项", ConfirmedMinutes = 60 }).ToArray(),
                Reports = [source.Reports[0] with { WorkSummary = "覆盖后的周报" }]
            };
            var overwrite = await service.PrepareImportAsync(changed);
            var result = await service.ApplyImportAsync(overwrite, BackupConflictPolicy.Overwrite, true);
            Assert.Equal(4, result.Overwritten);
            Assert.Equal(2, result.Added);
            var bound = await projects.FindAsync(projectId);
            Assert.NotNull(bound);
            Assert.True(bound.IsBound);
            Assert.Equal(binding.RepositoryPath, bound.RepositoryPath);
            Assert.Equal(binding.Author, bound.Author);
            Assert.Equal("覆盖后的项目", bound.Name);

            var afterOverwrite = await service.CaptureAsync();
            Assert.Equal("覆盖后的手工记录", afterOverwrite.Entries.Single(entry => entry.Id == manualId).Description);
            Assert.Equal("覆盖后的周报", afterOverwrite.Reports.Single(report => report.Id == reportId).WorkSummary);
            var stale = await service.PrepareImportAsync(changed);
            var entries = new SqlSugarWorkEntryRepository(configuration);
            var existing = await entries.FindAsync(manualId);
            Assert.NotNull(existing);
            var manuallyChanged = new WorkEntry(existing.Id, existing.WorkDate, "预览后的修改",
                existing.CreatedAtUtc, existing.UpdatedAtUtc.AddTicks(10), existing.ConfirmedMinutes);
            Assert.True(await entries.UpdateAsync(manuallyChanged));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ApplyImportAsync(stale, BackupConflictPolicy.Overwrite, true));
            Assert.Equal("预览后的修改", (await entries.FindAsync(manualId))!.Content);
        }
        finally
        {
            await CleanupAsync(settings, projectId, manualId, reportId);
        }
    }

    [MySqlIntegrationFact]
    public async Task Import_FailureAfterEarlierWritesRollsBackEverything()
    {
        var settings = MySqlIntegrationSupport.LoadSettings();
        await new MySqlDatabaseInitializer().InitializeAsync(settings);
        var store = new SqlSugarBackupStore(new MySqlIntegrationSupport.FixedConfigurationStore(settings));
        var service = new BackupService(store, new BackupImportPlanner(), TimeProvider.System);
        var source = CreateBackup(FindUnusedWeek(await service.CaptureAsync()));
        var projectId = source.Projects[0].Id;
        var manualId = source.Entries.Single(entry => entry.Kind == BackupEntryKind.Manual).Id;
        var reportId = source.Reports[0].Id;
        var trigger = "wr_test_" + Guid.NewGuid().ToString("N");
        var triggerCreated = false;

        try
        {
            var plan = await service.PrepareImportAsync(source);
            await using (var connection = MySqlIntegrationSupport.CreateConnection(settings))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE TRIGGER `{trigger}` BEFORE INSERT ON weekly_reports FOR EACH ROW " +
                    $"BEGIN IF NEW.id = '{reportId:D}' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'synthetic failure'; END IF; END";
                await command.ExecuteNonQueryAsync();
                triggerCreated = true;
            }

            await Assert.ThrowsAsync<DatabaseAccessException>(
                () => service.ApplyImportAsync(plan, BackupConflictPolicy.Skip, true));
            var after = await service.CaptureAsync();
            Assert.DoesNotContain(after.Projects, project => project.Id == projectId);
            Assert.DoesNotContain(after.Entries, entry => entry.Id == manualId);
            Assert.DoesNotContain(after.Reports, report => report.Id == reportId);
        }
        finally
        {
            if (triggerCreated)
            {
                await using var connection = MySqlIntegrationSupport.CreateConnection(settings);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TRIGGER `{trigger}`";
                await command.ExecuteNonQueryAsync();
            }

            await CleanupAsync(settings, projectId, manualId, reportId);
        }
    }

    [MySqlIntegrationFact]
    public async Task JsonAndExcel_MultipleWeeksRoundTripThroughFilesAndDatabase()
    {
        var settings = MySqlIntegrationSupport.LoadSettings();
        await new MySqlDatabaseInitializer().InitializeAsync(settings);
        var configuration = new MySqlIntegrationSupport.FixedConfigurationStore(settings);
        var service = new BackupService(new SqlSugarBackupStore(configuration), new BackupImportPlanner(), TimeProvider.System);
        var files = new BackupFileService(new JsonBackupCodec(), new ExcelBackupCodec());
        var directory = Path.Combine(Path.GetTempPath(), "WeeklyReport.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            foreach (var format in new[] { BackupFileFormat.Json, BackupFileFormat.Excel })
            {
                var existing = await service.CaptureAsync();
                var first = CreateBackup(FindUnusedWeek(existing));
                var second = CreateBackup(FindUnusedWeek(existing with
                {
                    Reports = existing.Reports.Concat(first.Reports).ToArray()
                }));
                var source = first with
                {
                    Projects = first.Projects.Concat(second.Projects).ToArray(),
                    Entries = first.Entries.Concat(second.Entries).ToArray(),
                    Commits = first.Commits.Concat(second.Commits).ToArray(),
                    Reports = first.Reports.Concat(second.Reports).ToArray()
                };
                var path = Path.Combine(directory, format == BackupFileFormat.Json ? "weeks.json" : "weeks.xlsx");

                try
                {
                    var initial = await service.PrepareImportAsync(source);
                    Assert.Equal(12, (await service.ApplyImportAsync(initial, BackupConflictPolicy.Skip, true)).Added);
                    var captured = await service.CaptureAsync();
                    var selected = BackupSelection.SelectWeeks(captured,
                        [first.Reports[0].WeekStart, second.Reports[0].WeekStart, first.Reports[0].WeekStart]);
                    Assert.Equal(2, selected.Reports.Count);
                    Assert.Equal(4, selected.Entries.Count);
                    Assert.Equal(4, selected.Commits.Count);

                    await files.ExportAsync(path, format, selected, overwrite: false);
                    var imported = await files.ImportAsync(path, format);
                    await CleanupAsync(settings, first.Projects[0].Id, first.Entries[0].Id, first.Reports[0].Id);
                    await CleanupAsync(settings, second.Projects[0].Id, second.Entries[0].Id, second.Reports[0].Id);

                    var restore = await service.PrepareImportAsync(imported);
                    Assert.Equal(12, (await service.ApplyImportAsync(restore, BackupConflictPolicy.Skip, true)).Added);
                    var afterRestore = BackupSelection.SelectWeeks(await service.CaptureAsync(),
                        [first.Reports[0].WeekStart, second.Reports[0].WeekStart]);
                    Assert.Equal(selected.Projects.OrderBy(item => item.Id), afterRestore.Projects.OrderBy(item => item.Id));
                    Assert.Equal(selected.Entries.OrderBy(item => item.Id), afterRestore.Entries.OrderBy(item => item.Id));
                    Assert.Equal(selected.Commits.OrderBy(item => item.Hash).ThenBy(item => item.ProjectId),
                        afterRestore.Commits.OrderBy(item => item.Hash).ThenBy(item => item.ProjectId));
                    Assert.Equal(selected.Reports.OrderBy(item => item.Id), afterRestore.Reports.OrderBy(item => item.Id));

                    var repeated = await service.PrepareImportAsync(imported);
                    Assert.Equal(12, (await service.ApplyImportAsync(repeated, BackupConflictPolicy.Skip, true)).Skipped);
                    var projects = new SqlSugarGitProjectRepository(configuration);
                    var binding = new GitProject(first.Projects[0].Id, first.Projects[0].Name,
                        Path.GetTempPath(), GitAuthorField.Email, "test@example.invalid");
                    Assert.True(await projects.UpdateAsync(binding));
                    var changed = imported with
                    {
                        Projects = imported.Projects.Select(project => project.Id == binding.Id
                            ? project with { Name = "覆盖后仍保留绑定" }
                            : project).ToArray()
                    };
                    var overwrite = await service.PrepareImportAsync(changed);
                    await service.ApplyImportAsync(overwrite, BackupConflictPolicy.Overwrite, true);
                    var rebound = await projects.FindAsync(binding.Id);
                    Assert.NotNull(rebound);
                    Assert.Equal(binding.RepositoryPath, rebound.RepositoryPath);
                    Assert.Equal(binding.Author, rebound.Author);
                    Assert.Equal("覆盖后仍保留绑定", rebound.Name);
                }
                finally
                {
                    await CleanupAsync(settings, first.Projects[0].Id, first.Entries[0].Id, first.Reports[0].Id);
                    await CleanupAsync(settings, second.Projects[0].Id, second.Entries[0].Id, second.Reports[0].Id);
                    File.Delete(path);
                }
            }
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    private static BackupDocument CreateBackup(DateOnly week)
    {
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var project = Guid.NewGuid();
        var manual = Guid.NewGuid();
        var git = Guid.NewGuid();
        var atNine = new DateTimeOffset(week.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
        return new BackupDocument(
            1, now,
            [new BackupProject(project, "备份项目")],
            [
                new BackupEntry(manual, BackupEntryKind.Manual, null, week, "手工记录", null, 75, false, now, now),
                new BackupEntry(git, BackupEntryKind.Git, project, week, "Git 工作项", 60, 45, false, null, null)
            ],
            [
                new BackupCommit(project, git, new string('a', 40), "name", "test@example.invalid", atNine, "one"),
                new BackupCommit(project, git, new string('b', 40), "name", "test@example.invalid", atNine.AddHours(1), "two")
            ],
            [new BackupReport(Guid.NewGuid(), week, week.AddDays(6), "本周", "下周", "风险", 120, 0, 0, now, now)]);
    }

    private static DateOnly FindUnusedWeek(BackupDocument current)
    {
        var existing = current.Reports.Select(report => report.WeekStart).ToHashSet();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var week = new DateOnly(2000, 1, 3).AddDays(RandomNumberGenerator.GetInt32(0, 5000) * 7);
            if (!existing.Contains(week))
            {
                return week;
            }
        }

        throw new InvalidOperationException("无法在隔离测试库中找到空闲周。");
    }

    private static async Task CleanupAsync(DatabaseConnectionSettings settings, Guid project, Guid manual, Guid report)
    {
        await using var connection = MySqlIntegrationSupport.CreateConnection(settings);
        await connection.OpenAsync();
        foreach (var statement in new[]
        {
            "DELETE FROM git_commit_links WHERE project_id = @project",
            "DELETE FROM git_work_items WHERE project_id = @project",
            "DELETE FROM git_projects WHERE id = @project",
            "DELETE FROM work_entries WHERE id = @manual",
            "DELETE FROM weekly_reports WHERE id = @report"
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.Parameters.AddWithValue("@project", project.ToString("D"));
            command.Parameters.AddWithValue("@manual", manual.ToString("D"));
            command.Parameters.AddWithValue("@report", report.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }
    }
}
