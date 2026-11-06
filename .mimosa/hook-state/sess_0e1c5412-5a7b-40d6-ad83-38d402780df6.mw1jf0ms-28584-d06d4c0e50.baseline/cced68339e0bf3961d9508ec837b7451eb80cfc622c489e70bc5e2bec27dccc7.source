using SqlSugar;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Persistence;

public sealed class SqlSugarBackupStore(IDatabaseConfigurationStore configurationStore) : IBackupStore
{
    private readonly SqlSugarExecutor _executor = new(configurationStore);

    public async Task<BackupDocument> CaptureAsync(DateTimeOffset exportedAtUtc)
    {
        if (exportedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("导出时间必须为 UTC。", nameof(exportedAtUtc));
        }

        return await _executor.ExecuteAsync(database =>
            SqlSugarTransactions.SerializableAsync(database, async () =>
                (await CaptureLockedAsync(database, exportedAtUtc)).Document));
    }

    public async Task<BackupImportResult> ImportAsync(PreparedBackupImport import, BackupConflictPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(import);
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        BackupValidator.Validate(import.Incoming);
        BackupValidator.Validate(import.Baseline);
        return await _executor.ExecuteAsync(database =>
            SqlSugarTransactions.SerializableAsync(database, async () =>
            {
                var current = await CaptureLockedAsync(database, import.Baseline.ExportedAtUtc);
                if (!Equivalent(import.Baseline, current.Document))
                {
                    throw new InvalidOperationException("数据库内容已发生变化，请重新预览导入。");
                }

                _ = new BackupImportPlanner().Prepare(import.Incoming, current.Document);
                return await ImportLockedAsync(database, import.Incoming, current, policy);
            }));
    }

    private static async Task<CapturedState> CaptureLockedAsync(SqlSugarClient database, DateTimeOffset exportedAtUtc)
    {
        var projects = await SqlSugarGitSyncStore.LockProjectsAsync(database);
        var manualRows = await database.Ado.SqlQueryAsync<WorkEntryRow>(
            "SELECT id AS Id, work_date AS WorkDate, content AS Content, created_at_utc AS CreatedAtUtc, " +
            "updated_at_utc AS UpdatedAtUtc, confirmed_minutes AS ConfirmedMinutes " +
            "FROM work_entries ORDER BY id FOR UPDATE");
        var gitItems = await SqlSugarGitSyncStore.LoadSnapshotsAsync(database, lockRows: true);
        var reportRows = await database.Ado.SqlQueryAsync<WeeklyReportRow>(
            "SELECT id AS Id, week_start AS WeekStart, week_end AS WeekEnd, work_summary AS WorkSummary, " +
            "next_week_plan AS NextWeekPlan, risks AS Risks, confirmed_minutes AS ConfirmedMinutes, " +
            "estimated_minutes AS EstimatedMinutes, unknown_hours_count AS UnknownHoursCount, " +
            "created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc " +
            "FROM weekly_reports ORDER BY id FOR UPDATE");

        var entries = manualRows.Select(SqlSugarWorkEntryRepository.ToEntry)
            .Select(entry => new BackupEntry(
                entry.Id, BackupEntryKind.Manual, null, entry.WorkDate, entry.Content,
                null, entry.ConfirmedMinutes, false, entry.CreatedAtUtc, entry.UpdatedAtUtc))
            .Concat(gitItems.Select(item => new BackupEntry(
                item.Id, BackupEntryKind.Git, item.ProjectId, item.WorkDate, item.Description,
                item.EstimatedMinutes, item.ConfirmedMinutes, item.IsDeleted, null, null)))
            .ToArray();
        var commits = gitItems.SelectMany(item => item.Commits.Select(commit => new BackupCommit(
            commit.ProjectId, item.Id, commit.Hash, commit.AuthorName, commit.AuthorEmail, commit.AuthoredAt, commit.Subject)))
            .ToArray();
        var reports = reportRows.Select(SqlSugarWeeklyReportRepository.ToReport)
            .Select(report => new BackupReport(
                report.Id, report.Week.Start, report.Week.EndInclusive, report.WorkSummary,
                report.NextWeekPlan, report.Risks, report.ConfirmedMinutes, report.EstimatedMinutes,
                report.UnknownHoursCount, report.CreatedAtUtc, report.UpdatedAtUtc))
            .ToArray();
        var document = new BackupDocument(
            BackupDocument.CurrentFormatVersion,
            exportedAtUtc,
            projects.Select(project => new BackupProject(Guid.Parse(project.Id), project.Name)).ToArray(),
            entries,
            commits,
            reports);
        BackupValidator.Validate(document);
        return new CapturedState(document, gitItems);
    }

    private static async Task<BackupImportResult> ImportLockedAsync(
        SqlSugarClient database,
        BackupDocument incoming,
        CapturedState current,
        BackupConflictPolicy policy)
    {
        var added = 0;
        var overwritten = 0;
        var skipped = 0;
        var currentProjects = current.Document.Projects.ToDictionary(item => item.Id);
        var currentEntries = current.Document.Entries.ToDictionary(item => item.Id);
        var currentGitDays = current.Document.Entries.Where(item => item.Kind == BackupEntryKind.Git)
            .ToDictionary(item => (item.ProjectId!.Value, item.WorkDate));
        var currentGitItems = current.GitItems.ToDictionary(item => item.Id);
        var currentReports = current.Document.Reports.ToDictionary(item => item.WeekStart);
        var gitIdMap = new Dictionary<Guid, Guid?>();

        foreach (var project in incoming.Projects)
        {
            var row = new GitProjectRow { Id = project.Id.ToString("D"), Name = project.Name };
            if (currentProjects.ContainsKey(project.Id))
            {
                if (policy == BackupConflictPolicy.Skip)
                {
                    skipped++;
                    continue;
                }

                await database.Updateable(row).UpdateColumns(value => new { value.Name }).ExecuteCommandAsync();
                overwritten++;
            }
            else
            {
                await database.Insertable(row).ExecuteCommandAsync();
                added++;
            }
        }

        foreach (var item in incoming.Entries)
        {
            if (item.Kind == BackupEntryKind.Manual)
            {
                var exists = currentEntries.TryGetValue(item.Id, out var existing);
                if (exists && policy == BackupConflictPolicy.Skip)
                {
                    skipped++;
                    continue;
                }

                var updatedAt = exists
                    ? NextUpdateTime(existing!.UpdatedAtUtc!.Value, item.CreatedAtUtc!.Value)
                    : item.UpdatedAtUtc!.Value;
                var row = new WorkEntryRow
                {
                    Id = item.Id.ToString("D"),
                    WorkDate = item.WorkDate.ToDateTime(TimeOnly.MinValue),
                    Content = item.Description,
                    ConfirmedMinutes = item.ConfirmedMinutes,
                    CreatedAtUtc = item.CreatedAtUtc!.Value.UtcDateTime,
                    UpdatedAtUtc = updatedAt.UtcDateTime
                };
                if (exists)
                {
                    await database.Updateable(row)
                        .UpdateColumns(value => new
                        {
                            value.WorkDate, value.Content, value.ConfirmedMinutes, value.CreatedAtUtc, value.UpdatedAtUtc
                        })
                        .ExecuteCommandAsync();
                    overwritten++;
                }
                else
                {
                    await database.Insertable(row).ExecuteCommandAsync();
                    added++;
                }

                continue;
            }

            var group = (item.ProjectId!.Value, item.WorkDate);
            if (currentGitDays.TryGetValue(group, out var existingGit))
            {
                if (policy == BackupConflictPolicy.Skip)
                {
                    gitIdMap[item.Id] = null;
                    skipped++;
                    continue;
                }

                var snapshot = currentGitItems[existingGit.Id];
                var targetId = existingGit.Id.ToString("D");
                await database.Deleteable<GitCommitRow>()
                    .Where(link => link.WorkItemId == targetId)
                    .ExecuteCommandAsync();
                var row = ToGitRow(item, existingGit.Id, checked(snapshot.Revision + 1));
                await database.Updateable(row)
                    .UpdateColumns(value => new
                    {
                        value.Description, value.EstimatedMinutes, value.ConfirmedMinutes, value.IsDeleted, value.Revision
                    })
                    .ExecuteCommandAsync();
                gitIdMap[item.Id] = existingGit.Id;
                overwritten++;
            }
            else
            {
                await database.Insertable(ToGitRow(item, item.Id, 0)).ExecuteCommandAsync();
                gitIdMap[item.Id] = item.Id;
                added++;
            }
        }

        foreach (var commit in incoming.Commits)
        {
            if (!gitIdMap.TryGetValue(commit.WorkItemId, out var target))
            {
                throw new BackupFormatException("Git 提交缺少对应的工作项。");
            }

            if (target is null)
            {
                skipped++;
                continue;
            }

            var domainCommit = new GitCommit(
                commit.ProjectId, commit.Hash, commit.AuthorName, commit.AuthorEmail, commit.AuthoredAt, commit.Subject);
            await database.Insertable(SqlSugarGitSyncStore.ToRow(domainCommit, target.Value)).ExecuteCommandAsync();
            added++;
        }

        foreach (var report in incoming.Reports)
        {
            if (currentReports.TryGetValue(report.WeekStart, out var existing))
            {
                if (policy == BackupConflictPolicy.Skip)
                {
                    skipped++;
                    continue;
                }

                var row = ToReportRow(report, existing.Id, NextUpdateTime(existing.UpdatedAtUtc, report.CreatedAtUtc));
                await database.Updateable(row)
                    .UpdateColumns(value => new
                    {
                        value.WeekEnd, value.WorkSummary, value.NextWeekPlan, value.Risks,
                        value.ConfirmedMinutes, value.EstimatedMinutes, value.UnknownHoursCount,
                        value.CreatedAtUtc, value.UpdatedAtUtc
                    })
                    .ExecuteCommandAsync();
                overwritten++;
            }
            else
            {
                await database.Insertable(ToReportRow(report, report.Id, report.UpdatedAtUtc)).ExecuteCommandAsync();
                added++;
            }
        }

        return new BackupImportResult(added, overwritten, skipped);
    }

    private static GitWorkItemRow ToGitRow(BackupEntry item, Guid targetId, long revision) => new()
    {
        Id = targetId.ToString("D"),
        ProjectId = item.ProjectId!.Value.ToString("D"),
        WorkDate = item.WorkDate.ToDateTime(TimeOnly.MinValue),
        Description = item.Description,
        EstimatedMinutes = item.EstimatedMinutes,
        ConfirmedMinutes = item.ConfirmedMinutes,
        IsDeleted = item.IsDeleted,
        Revision = revision
    };

    private static WeeklyReportRow ToReportRow(BackupReport report, Guid targetId, DateTimeOffset updatedAt) => new()
    {
        Id = targetId.ToString("D"),
        WeekStart = report.WeekStart.ToDateTime(TimeOnly.MinValue),
        WeekEnd = report.WeekEnd.ToDateTime(TimeOnly.MinValue),
        WorkSummary = report.WorkSummary,
        NextWeekPlan = report.NextWeekPlan,
        Risks = report.Risks,
        ConfirmedMinutes = report.ConfirmedMinutes,
        EstimatedMinutes = report.EstimatedMinutes,
        UnknownHoursCount = report.UnknownHoursCount,
        CreatedAtUtc = report.CreatedAtUtc.UtcDateTime,
        UpdatedAtUtc = updatedAt.UtcDateTime
    };

    private static DateTimeOffset NextUpdateTime(DateTimeOffset existing, DateTimeOffset importedCreation)
    {
        var now = StorageDateRange.NormalizeUtcTimestamp(DateTimeOffset.UtcNow);
        var next = existing.AddTicks(10);
        if (now < next)
        {
            now = next;
        }

        return now < importedCreation ? importedCreation : now;
    }

    private static bool Equivalent(BackupDocument first, BackupDocument second) =>
        first.FormatVersion == second.FormatVersion
        && first.Projects.OrderBy(item => item.Id).SequenceEqual(second.Projects.OrderBy(item => item.Id))
        && first.Entries.OrderBy(item => item.Id).SequenceEqual(second.Entries.OrderBy(item => item.Id))
        && SameCommits(first.Commits, second.Commits)
        && first.Reports.OrderBy(item => item.Id).SequenceEqual(second.Reports.OrderBy(item => item.Id));

    private static bool SameCommits(IReadOnlyList<BackupCommit> first, IReadOnlyList<BackupCommit> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        var actual = second.ToDictionary(item => (item.ProjectId, item.Hash));
        return first.All(item => actual.TryGetValue((item.ProjectId, item.Hash), out var other)
            && item.WorkItemId == other.WorkItemId
            && item.AuthorName == other.AuthorName
            && item.AuthorEmail == other.AuthorEmail
            && item.AuthoredAt.EqualsExact(other.AuthoredAt)
            && item.Subject == other.Subject);
    }

    private sealed record CapturedState(BackupDocument Document, IReadOnlyList<GitWorkItemSnapshot> GitItems);
}
