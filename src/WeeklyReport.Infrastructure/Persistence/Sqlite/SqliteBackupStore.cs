// [新增-备份离线化] 备份计划切片 2：SQLite 版备份仓储——捕获/导入均走本地有效视图，导入在同一
// RunInOperationAsync 事务内完成基线比较 + 增删改 + sync_operation 入队（一次导入 = 一个操作组）。
// 与 MySQL 版语义一致：覆盖保留本机绑定、项目/日期自然键冲突跳过或按稳定 ID 覆盖、
// 周报按周起始唯一、基线变化拒绝导入。Git 覆盖时修订号 +1、清空提交链接后重建。
using System.Globalization;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

public sealed class SqliteBackupStore(string databasePath) : IBackupStore
{
    public async Task<BackupDocument> CaptureAsync(DateTimeOffset exportedAtUtc)
    {
        if (exportedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("导出时间必须为 UTC。", nameof(exportedAtUtc));
        }

        var store = new SqliteLocalStore(databasePath);
        _ = await store.GetStateAsync(); // 初始化幂等 + 损坏文件在边界包装
        return await new SqliteExecutor(databasePath).ExecuteAsync(database =>
            SqlSugarTransactions.SerializableAsync(database, async () =>
                (await CaptureFromSessionAsync(database, exportedAtUtc)).Document));
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

        // 整次导入 = 一个操作组：基线比较 + 全部增删改 + 入队同事务
        return await new SqliteOperationQueue(databasePath).RunInOperationAsync(
            "backup.import", async session =>
            {
                var current = await CaptureFromSessionAsync(session.Database, import.Baseline.ExportedAtUtc);
                if (!Equivalent(import.Baseline, current.Document))
                {
                    throw new InvalidOperationException("数据库内容已发生变化，请重新预览导入。");
                }

                var result = await ApplyImportAsync(session.Database, import.Incoming, current, policy);
                session.Record(new LocalOperationChange(
                    "backup_import", "full", "import", LocalObjectChange.Update));
                return result;
            });
    }

    // —— 捕获（事务内，经 session.Database 读取一致快照）——
    private static async Task<CapturedState> CaptureFromSessionAsync(
        SqlSugar.SqlSugarClient database, DateTimeOffset exportedAtUtc)
    {
        var projectRows = await database.Ado.SqlQueryAsync<GitProjectRow>(
            "SELECT id AS Id, name AS Name, repository_path AS RepositoryPath, " +
            "author_field AS AuthorField, author_value AS Author FROM git_projects ORDER BY id");
        var manualRows = await database.Ado.SqlQueryAsync<SqliteWorkEntryRow>(
            "SELECT id AS Id, work_date AS WorkDate, content AS Content, created_at_utc AS CreatedAtUtc, " +
            "updated_at_utc AS UpdatedAtUtc, confirmed_minutes AS ConfirmedMinutes " +
            "FROM work_entries ORDER BY id");
        var gitItems = await LoadGitSnapshotsAsync(database);
        var reportRows = await database.Ado.SqlQueryAsync<SqliteWeeklyReportRow>(
            "SELECT id AS Id, week_start AS WeekStart, week_end AS WeekEnd, work_summary AS WorkSummary, " +
            "next_week_plan AS NextWeekPlan, risks AS Risks, confirmed_minutes AS ConfirmedMinutes, " +
            "estimated_minutes AS EstimatedMinutes, unknown_hours_count AS UnknownHoursCount, " +
            "created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc " +
            "FROM weekly_reports ORDER BY id");

        var entries = manualRows.Select(row => new BackupEntry(
            Guid.Parse(row.Id), BackupEntryKind.Manual, null,
            DateOnly.ParseExact(row.WorkDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), row.Content,
            null, row.ConfirmedMinutes, false,
            SqliteTimestamp.Parse(row.CreatedAtUtc), SqliteTimestamp.Parse(row.UpdatedAtUtc)))
        .Concat(gitItems.Select(item => new BackupEntry(
            item.Id, BackupEntryKind.Git, item.ProjectId, item.WorkDate, item.Description,
            item.EstimatedMinutes, item.ConfirmedMinutes, item.IsDeleted, null, null)))
        .ToArray();
        var commits = gitItems.SelectMany(item => item.Commits.Select(commit => new BackupCommit(
            commit.ProjectId, item.Id, commit.Hash, commit.AuthorName, commit.AuthorEmail,
            commit.AuthoredAt, commit.Subject)))
        .ToArray();
        var reports = reportRows.Select(row => new BackupReport(
            Guid.Parse(row.Id),
            DateOnly.ParseExact(row.WeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(row.WeekEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            row.WorkSummary, row.NextWeekPlan, row.Risks,
            row.ConfirmedMinutes, row.EstimatedMinutes, row.UnknownHoursCount,
            SqliteTimestamp.Parse(row.CreatedAtUtc), SqliteTimestamp.Parse(row.UpdatedAtUtc)))
        .ToArray();
        var document = new BackupDocument(
            BackupDocument.CurrentFormatVersion, exportedAtUtc,
            projectRows.Select(row => new BackupProject(Guid.Parse(row.Id), row.Name)).ToArray(),
            entries, commits, reports);
        BackupValidator.Validate(document);
        return new CapturedState(document, gitItems);
    }

    private static async Task<IReadOnlyList<GitWorkItemSnapshot>> LoadGitSnapshotsAsync(
        SqlSugar.SqlSugarClient database)
    {
        var items = await database.Ado.SqlQueryAsync<SqliteGitWorkItemRow>(
            "SELECT id AS Id, project_id AS ProjectId, work_date AS WorkDate, description AS Description, " +
            "estimated_minutes AS EstimatedMinutes, confirmed_minutes AS ConfirmedMinutes, " +
            "is_deleted AS IsDeleted, revision AS Revision FROM git_work_items ORDER BY id");
        var links = await database.Ado.SqlQueryAsync<GitCommitRow>(
            "SELECT project_id AS ProjectId, commit_hash AS CommitHash, work_item_id AS WorkItemId, " +
            "author_name AS AuthorName, author_email AS AuthorEmail, authored_at AS AuthoredAt, " +
            "subject AS Subject FROM git_commit_links ORDER BY project_id, commit_hash");

        var commitsByItem = links.GroupBy(link => link.WorkItemId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(ToCommit).ToArray(), StringComparer.OrdinalIgnoreCase);
        return items.Select(item => new GitWorkItemSnapshot(
            Guid.Parse(item.Id),
            Guid.Parse(item.ProjectId),
            DateOnly.ParseExact(item.WorkDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            item.Description,
            item.ConfirmedMinutes,
            item.IsDeleted,
            commitsByItem.GetValueOrDefault(item.Id) ?? [],
            item.EstimatedMinutes,
            item.Revision)).ToArray();
    }

    private static GitCommit ToCommit(GitCommitRow row)
    {
        if (!DateTimeOffset.TryParseExact(row.AuthoredAt, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var authoredAt))
        {
            throw new InvalidDataException("Git 提交的作者时间无效。");
        }

        return new GitCommit(Guid.Parse(row.ProjectId), row.CommitHash, row.AuthorName, row.AuthorEmail, authoredAt, row.Subject);
    }

    // —— 导入（事务内应用全部变更）——
    private static async Task<BackupImportResult> ApplyImportAsync(
        SqlSugar.SqlSugarClient database,
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
            var id = project.Id.ToString("D");
            if (currentProjects.ContainsKey(project.Id))
            {
                if (policy == BackupConflictPolicy.Skip)
                {
                    skipped++;
                    continue;
                }

                await database.Ado.ExecuteCommandAsync(
                    "UPDATE git_projects SET name = @name WHERE id = @id", new { id, name = project.Name });
                overwritten++;
            }
            else
            {
                await database.Ado.ExecuteCommandAsync(
                    "INSERT INTO git_projects (id, name) VALUES (@id, @name)", new { id, name = project.Name });
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
                    ? StorageDateRange.GetNextTimestamp(
                        existing!.UpdatedAtUtc!.Value, item.CreatedAtUtc!.Value, DateTimeOffset.UtcNow)
                    : item.UpdatedAtUtc!.Value;
                var id = item.Id.ToString("D");
                var workDate = item.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var created = SqliteTimestamp.Format(item.CreatedAtUtc!.Value);
                var updated = SqliteTimestamp.Format(updatedAt);
                if (exists)
                {
                    await database.Ado.ExecuteCommandAsync(
                        "UPDATE work_entries SET work_date = @workDate, content = @content, " +
                        "confirmed_minutes = @confirmedMinutes, created_at_utc = @created, updated_at_utc = @updated " +
                        "WHERE id = @id",
                        new { id, workDate, content = item.Description, confirmedMinutes = item.ConfirmedMinutes, created, updated });
                    overwritten++;
                }
                else
                {
                    await database.Ado.ExecuteCommandAsync(
                        "INSERT INTO work_entries (id, work_date, content, created_at_utc, updated_at_utc, confirmed_minutes) " +
                        "VALUES (@id, @workDate, @content, @created, @updated, @confirmedMinutes)",
                        new { id, workDate, content = item.Description, created, updated, confirmedMinutes = item.ConfirmedMinutes });
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
                await database.Ado.ExecuteCommandAsync(
                    "DELETE FROM git_commit_links WHERE work_item_id = @targetId", new { targetId });
                var revision = checked(snapshot.Revision + 1);
                var workDateStr = item.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                await database.Ado.ExecuteCommandAsync(
                    "UPDATE git_work_items SET description = @desc, estimated_minutes = @est, " +
                    "confirmed_minutes = @conf, is_deleted = @del, revision = @rev " +
                    "WHERE id = @targetId",
                    new
                    {
                        targetId,
                        desc = item.Description,
                        est = item.EstimatedMinutes,
                        conf = item.ConfirmedMinutes,
                        del = item.IsDeleted ? 1 : 0,
                        rev = revision
                    });
                gitIdMap[item.Id] = existingGit.Id;
                overwritten++;
            }
            else
            {
                var id = item.Id.ToString("D");
                var projectId = item.ProjectId!.Value.ToString("D");
                var workDateStr = item.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                await database.Ado.ExecuteCommandAsync(
                    "INSERT INTO git_work_items (id, project_id, work_date, description, estimated_minutes, " +
                    "confirmed_minutes, is_deleted, revision) VALUES (@id, @projectId, @workDate, @desc, @est, @conf, @del, 0)",
                    new { id, projectId, workDate = workDateStr, desc = item.Description, est = item.EstimatedMinutes, conf = item.ConfirmedMinutes, del = item.IsDeleted ? 1 : 0 });
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

            var projectId = commit.ProjectId.ToString("D");
            var workItemId = target.Value.ToString("D");
            await database.Ado.ExecuteCommandAsync(
                "INSERT INTO git_commit_links (project_id, commit_hash, work_item_id, author_name, " +
                "author_email, authored_at, subject) VALUES (@projectId, @hash, @workItemId, @name, @email, @at, @subject)",
                new
                {
                    projectId,
                    hash = commit.Hash,
                    workItemId,
                    name = commit.AuthorName,
                    email = commit.AuthorEmail,
                    at = commit.AuthoredAt.ToString("O", CultureInfo.InvariantCulture),
                    subject = commit.Subject
                });
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

                var id = existing.Id.ToString("D");
                var updatedAt = StorageDateRange.GetNextTimestamp(
                    existing.UpdatedAtUtc, report.CreatedAtUtc, DateTimeOffset.UtcNow);
                var updated = SqliteTimestamp.Format(updatedAt);
                var weekStart = report.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var weekEnd = report.WeekEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var created = SqliteTimestamp.Format(report.CreatedAtUtc);
                await database.Ado.ExecuteCommandAsync(
                    "UPDATE weekly_reports SET week_start = @weekStart, week_end = @weekEnd, " +
                    "work_summary = @summary, next_week_plan = @plan, risks = @risks, " +
                    "confirmed_minutes = @conf, estimated_minutes = @est, unknown_hours_count = @unknown, " +
                    "created_at_utc = @created, updated_at_utc = @updated WHERE id = @id",
                    new
                    {
                        id, weekStart, weekEnd,
                        summary = report.WorkSummary, plan = report.NextWeekPlan, risks = report.Risks,
                        conf = report.ConfirmedMinutes, est = report.EstimatedMinutes,
                        unknown = report.UnknownHoursCount, created, updated
                    });
                overwritten++;
            }
            else
            {
                var id = report.Id.ToString("D");
                var weekStart = report.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var weekEnd = report.WeekEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var created = SqliteTimestamp.Format(report.CreatedAtUtc);
                var updated = SqliteTimestamp.Format(report.UpdatedAtUtc);
                await database.Ado.ExecuteCommandAsync(
                    "INSERT INTO weekly_reports (id, week_start, week_end, work_summary, next_week_plan, risks, " +
                    "confirmed_minutes, estimated_minutes, unknown_hours_count, created_at_utc, updated_at_utc) " +
                    "VALUES (@id, @weekStart, @weekEnd, @summary, @plan, @risks, " +
                    "@conf, @est, @unknown, @created, @updated)",
                    new
                    {
                        id, weekStart, weekEnd,
                        summary = report.WorkSummary, plan = report.NextWeekPlan, risks = report.Risks,
                        conf = report.ConfirmedMinutes, est = report.EstimatedMinutes,
                        unknown = report.UnknownHoursCount, created, updated
                    });
                added++;
            }
        }

        return new BackupImportResult(added, overwritten, skipped);
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
