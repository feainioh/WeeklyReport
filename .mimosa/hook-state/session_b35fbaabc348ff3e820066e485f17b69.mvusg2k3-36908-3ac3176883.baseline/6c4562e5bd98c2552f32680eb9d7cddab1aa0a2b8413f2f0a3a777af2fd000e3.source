using System.Globalization;
using SqlSugar;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Persistence;

public sealed class SqlSugarGitSyncStore(IDatabaseConfigurationStore configurationStore) : IGitSyncStore
{
    private readonly SqlSugarExecutor _executor = new(configurationStore);

    public async Task<IReadOnlyList<GitWorkItemSnapshot>> LoadAllAsync() =>
        await _executor.ExecuteAsync<IReadOnlyList<GitWorkItemSnapshot>>(
            database => LoadSnapshotsAsync(database, lockRows: false));

    public async Task<IReadOnlyList<GitReportWorkItem>> GetByWeekAsync(WeekPeriod week) =>
        await _executor.ExecuteAsync<IReadOnlyList<GitReportWorkItem>>(async database =>
        {
            var start = week.Start.ToDateTime(TimeOnly.MinValue);
            var end = week.EndExclusive.ToDateTime(TimeOnly.MinValue);
            var rows = await database.Queryable<GitWorkItemRow>()
                .Where(row => row.WorkDate >= start && row.WorkDate < end && !row.IsDeleted)
                .OrderBy(row => row.WorkDate, OrderByType.Asc)
                .ToListAsync();
            var projects = await database.Queryable<GitProjectRow>().ToListAsync();
            var names = projects.ToDictionary(row => row.Id, row => row.Name, StringComparer.OrdinalIgnoreCase);
            return rows.Select(row =>
            {
                if (!names.TryGetValue(row.ProjectId, out var name))
                {
                    throw new InvalidDataException("Git 工作项关联的项目不存在。");
                }

                return new GitReportWorkItem(
                    Guid.Parse(row.Id), Guid.Parse(row.ProjectId), name, DateOnly.FromDateTime(row.WorkDate),
                    row.Description, row.EstimatedMinutes, row.ConfirmedMinutes);
            }).ToArray();
        });

    public async Task<GitSyncApplyResult> ApplyAsync(PreparedGitSync sync)
    {
        ArgumentNullException.ThrowIfNull(sync);
        if (sync.Preview.Proposals.Count == 0)
        {
            return new GitSyncApplyResult(0, 0, sync.Preview.DuplicateCount, sync.Preview.DeletedConflicts.Count);
        }

        return await _executor.ExecuteAsync(database => SqlSugarTransactions.SerializableAsync(database, async () =>
        {
            var projectRows = await LockProjectsAsync(database);
            EnsureProjectsUnchanged(sync.Projects, projectRows);
            var current = await LoadSnapshotsAsync(database, lockRows: true);
            EnsureBaselineUnchanged(sync.Baseline, current);
            return await ApplyProposalsAsync(database, sync, current);
        }));
    }

    public async Task<bool> UpdateWorkItemAsync(Guid id, string description, int? confirmedMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (confirmedMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmedMinutes), "工时不能为负数。");
        }

        DbTextLimits.Validate(description, "工作项描述");
        return await ChangeItemAsync(id, row =>
        {
            row.Description = description.Trim();
            row.ConfirmedMinutes = confirmedMinutes;
        });
    }

    public async Task<bool> SoftDeleteWorkItemAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("工作项标识不能为空。", nameof(id));
        }

        return await _executor.ExecuteAsync(database => SqlSugarTransactions.SerializableAsync(database, async () =>
        {
            await LockProjectsAsync(database);
            var current = await LoadSnapshotsAsync(database, lockRows: true);
            var item = current.SingleOrDefault(value => value.Id == id && !value.IsDeleted);
            if (item is null)
            {
                return false;
            }

            var row = new GitWorkItemRow
            {
                Id = item.Id.ToString("D"),
                Description = "（已删除）",
                EstimatedMinutes = null,
                ConfirmedMinutes = null,
                IsDeleted = true,
                Revision = checked(item.Revision + 1)
            };
            var key = row.Id;
            var revision = item.Revision;
            var deleted = await database.Updateable(row)
                .UpdateColumns(value => new
                {
                    value.Description, value.EstimatedMinutes, value.ConfirmedMinutes, value.IsDeleted, value.Revision
                })
                .Where(value => value.Id == key && value.Revision == revision && !value.IsDeleted)
                .ExecuteCommandAsync();
            if (deleted != 1)
            {
                throw new InvalidOperationException("Git 工作项已发生变化，请重新加载。");
            }

            await database.Ado.ExecuteCommandAsync(
                "UPDATE git_commit_links SET author_name = @empty, author_email = @empty, subject = @empty " +
                "WHERE work_item_id = @workItemId",
                new SugarParameter("@empty", string.Empty),
                new SugarParameter("@workItemId", key));

            await RecalculateAsync(database, current, item.WorkDate, id);
            return true;
        }));
    }

    public async Task<int> RecalculateDateAsync(DateOnly date)
    {
        StorageDateRange.ValidateWorkDate(date);
        return await _executor.ExecuteAsync(database => SqlSugarTransactions.SerializableAsync(database, async () =>
        {
            await LockProjectsAsync(database);
            var current = await LoadSnapshotsAsync(database, lockRows: true);
            return await RecalculateAsync(database, current, date, excludedId: null);
        }));
    }

    private static async Task<int> RecalculateAsync(
        SqlSugarClient database,
        IReadOnlyList<GitWorkItemSnapshot> current,
        DateOnly date,
        Guid? excludedId)
    {
        var remaining = current.Where(value => !value.IsDeleted && value.Id != excludedId && value.WorkDate == date)
            .ToArray();
        var hours = new GitHourEstimator().EstimateDated(
            remaining.SelectMany(value => value.Commits.Select(commit => new DatedGitCommit(commit, value.WorkDate))));
        var estimates = hours.SelectMany(day => day.Projects)
            .ToDictionary(project => project.ProjectId, project => project.Minutes);
        var changed = 0;
        foreach (var other in remaining)
        {
            var minutes = estimates.GetValueOrDefault(other.ProjectId);
            if (other.EstimatedMinutes != minutes)
            {
                await UpdateEstimateAsync(database, other, minutes);
                changed++;
            }
        }

        return changed;
    }

    internal static Task<List<GitProjectRow>> LockProjectsAsync(SqlSugarClient database) =>
        database.Ado.SqlQueryAsync<GitProjectRow>(
            "SELECT id AS Id, name AS Name, repository_path AS RepositoryPath, " +
            "author_field AS AuthorField, author_value AS Author FROM git_projects ORDER BY id FOR UPDATE");

    private async Task<bool> ChangeItemAsync(Guid id, Action<GitWorkItemRow> change)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("工作项标识不能为空。", nameof(id));
        }

        var key = id.ToString("D");
        return await _executor.ExecuteAsync(async database =>
        {
            var row = await database.Queryable<GitWorkItemRow>().FirstAsync(item => item.Id == key);
            if (row is null || row.IsDeleted)
            {
                return false;
            }

            var revision = row.Revision;
            change(row);
            row.Revision = checked(revision + 1);
            return await database.Updateable(row)
                .UpdateColumns(item => new { item.Description, item.ConfirmedMinutes, item.IsDeleted, item.Revision })
                .Where(item => item.Id == key && item.Revision == revision && !item.IsDeleted)
                .ExecuteCommandAsync() > 0;
        });
    }

    internal static async Task<IReadOnlyList<GitWorkItemSnapshot>> LoadSnapshotsAsync(
        SqlSugarClient database, bool lockRows)
    {
        List<GitWorkItemRow> items;
        List<GitCommitRow> links;
        if (lockRows)
        {
            items = await database.Ado.SqlQueryAsync<GitWorkItemRow>(
                "SELECT id AS Id, project_id AS ProjectId, work_date AS WorkDate, description AS Description, " +
                "estimated_minutes AS EstimatedMinutes, confirmed_minutes AS ConfirmedMinutes, " +
                "is_deleted AS IsDeleted, revision AS Revision FROM git_work_items ORDER BY id FOR UPDATE");
            links = await database.Ado.SqlQueryAsync<GitCommitRow>(
                "SELECT project_id AS ProjectId, commit_hash AS CommitHash, work_item_id AS WorkItemId, " +
                "author_name AS AuthorName, author_email AS AuthorEmail, authored_at AS AuthoredAt, " +
                "subject AS Subject FROM git_commit_links ORDER BY project_id, commit_hash FOR UPDATE");
        }
        else
        {
            items = await database.Queryable<GitWorkItemRow>().ToListAsync();
            links = await database.Queryable<GitCommitRow>().ToListAsync();
        }

        var itemIds = items.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (links.Any(link => !itemIds.Contains(link.WorkItemId)))
        {
            throw new InvalidDataException("Git 提交关联了不存在的工作项。");
        }

        var commitsByItem = links.GroupBy(link => link.WorkItemId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(ToCommit).ToArray(), StringComparer.OrdinalIgnoreCase);
        return items.Select(item => new GitWorkItemSnapshot(
            Guid.Parse(item.Id),
            Guid.Parse(item.ProjectId),
            DateOnly.FromDateTime(item.WorkDate),
            item.Description,
            item.ConfirmedMinutes,
            item.IsDeleted,
            commitsByItem.GetValueOrDefault(item.Id) ?? [],
            item.EstimatedMinutes,
            item.Revision)).ToArray();
    }

    private static GitCommit ToCommit(GitCommitRow row)
    {
        if (!DateTimeOffset.TryParseExact(
            row.AuthoredAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var authoredAt))
        {
            throw new InvalidDataException("Git 提交的作者时间无效。");
        }

        return new GitCommit(
            Guid.Parse(row.ProjectId), row.CommitHash, row.AuthorName, row.AuthorEmail, authoredAt, row.Subject);
    }

    private static void EnsureProjectsUnchanged(IReadOnlyList<GitProject> expected, IReadOnlyList<GitProjectRow> rows)
    {
        var current = rows.ToDictionary(row => Guid.Parse(row.Id));
        foreach (var project in expected)
        {
            if (!current.TryGetValue(project.Id, out var row))
            {
                throw new InvalidOperationException("Git 项目已发生变化，请重新预览。");
            }

            var actual = SqlSugarGitProjectRepository.ToProject(row);
            if (actual.Name != project.Name
                || actual.RepositoryPath != project.RepositoryPath
                || actual.AuthorField != project.AuthorField
                || actual.Author != project.Author)
            {
                throw new InvalidOperationException("Git 项目已发生变化，请重新预览。");
            }
        }
    }

    private static void EnsureBaselineUnchanged(
        IReadOnlyList<GitWorkItemSnapshot> expected, IReadOnlyList<GitWorkItemSnapshot> current)
    {
        var byId = current.ToDictionary(item => item.Id);
        if (expected.Count != current.Count)
        {
            throw new InvalidOperationException("Git 工作项已发生变化，请重新预览。");
        }

        foreach (var item in expected)
        {
            if (!byId.TryGetValue(item.Id, out var actual)
                || item.ProjectId != actual.ProjectId
                || item.WorkDate != actual.WorkDate
                || item.Description != actual.Description
                || item.ConfirmedMinutes != actual.ConfirmedMinutes
                || item.EstimatedMinutes != actual.EstimatedMinutes
                || item.IsDeleted != actual.IsDeleted
                || item.Revision != actual.Revision
                || !SameCommits(item.Commits, actual.Commits))
            {
                throw new InvalidOperationException("Git 工作项已发生变化，请重新预览。");
            }
        }
    }

    private static bool SameCommits(IReadOnlyList<GitCommit> expected, IReadOnlyList<GitCommit> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        var byHash = actual.ToDictionary(commit => commit.Hash, StringComparer.Ordinal);
        return expected.All(commit =>
            byHash.TryGetValue(commit.Hash, out var stored) && commit.HasSameMetadata(stored));
    }

    private static async Task<GitSyncApplyResult> ApplyProposalsAsync(
        SqlSugarClient database,
        PreparedGitSync sync,
        IReadOnlyList<GitWorkItemSnapshot> current)
    {
        var byDay = current.ToDictionary(item => (item.ProjectId, item.WorkDate));
        var estimates = sync.Preview.SuggestedHours
            .SelectMany(day => day.Projects.Select(project => (day.Date, project.ProjectId, project.Minutes)))
            .ToDictionary(item => (item.ProjectId, item.Date), item => item.Minutes);
        var updatedIds = new HashSet<Guid>();
        var created = 0;
        var updated = 0;

        foreach (var proposal in sync.Preview.Proposals)
        {
            var key = (proposal.ProjectId, proposal.WorkDate);
            if (!estimates.TryGetValue(key, out var estimatedMinutes))
            {
                throw new InvalidOperationException("Git 同步预览缺少工时估算，请重新预览。");
            }

            if (byDay.TryGetValue(key, out var existing))
            {
                if (existing.IsDeleted || existing.Id != proposal.WorkItemId)
                {
                    throw new InvalidOperationException("Git 工作项已发生变化，请重新预览。");
                }

                await UpdateEstimateAsync(database, existing, estimatedMinutes);
                updatedIds.Add(existing.Id);
                updated++;
            }
            else
            {
                DbTextLimits.Validate(proposal.SuggestedDescription, "Git 工作项描述");
                var row = new GitWorkItemRow
                {
                    Id = proposal.WorkItemId.ToString("D"),
                    ProjectId = proposal.ProjectId.ToString("D"),
                    WorkDate = proposal.WorkDate.ToDateTime(TimeOnly.MinValue),
                    Description = proposal.SuggestedDescription,
                    EstimatedMinutes = estimatedMinutes,
                    ConfirmedMinutes = null,
                    IsDeleted = false,
                    Revision = 0
                };
                await database.Insertable(row).ExecuteCommandAsync();
                created++;
            }

            var links = proposal.AddedCommits.Select(commit => ToRow(commit, proposal.WorkItemId)).ToArray();
            if (links.Length > 0)
            {
                await database.Insertable(links).ExecuteCommandAsync();
            }
        }

        foreach (var item in current.Where(item => !item.IsDeleted && !updatedIds.Contains(item.Id)))
        {
            if (estimates.TryGetValue((item.ProjectId, item.WorkDate), out var minutes)
                && item.EstimatedMinutes != minutes)
            {
                await UpdateEstimateAsync(database, item, minutes);
            }
        }

        return new GitSyncApplyResult(created, updated, sync.Preview.DuplicateCount, sync.Preview.DeletedConflicts.Count);
    }

    private static async Task UpdateEstimateAsync(SqlSugarClient database, GitWorkItemSnapshot item, int? minutes)
    {
        var row = new GitWorkItemRow
        {
            Id = item.Id.ToString("D"),
            EstimatedMinutes = minutes,
            Revision = checked(item.Revision + 1)
        };
        var id = row.Id;
        var revision = item.Revision;
        var updated = await database.Updateable(row)
            .UpdateColumns(value => new { value.EstimatedMinutes, value.Revision })
            .Where(value => value.Id == id && value.Revision == revision && !value.IsDeleted)
            .ExecuteCommandAsync();
        if (updated != 1)
        {
            throw new InvalidOperationException("Git 工作项已发生变化，请重新预览。");
        }
    }

    internal static GitCommitRow ToRow(GitCommit commit, Guid workItemId)
    {
        DbTextLimits.Validate(commit.AuthorName, "Git 作者");
        DbTextLimits.Validate(commit.AuthorEmail, "Git 作者邮箱");
        DbTextLimits.Validate(commit.Subject, "Git 提交摘要");
        return new GitCommitRow
        {
            ProjectId = commit.ProjectId.ToString("D"),
            CommitHash = commit.Hash,
            WorkItemId = workItemId.ToString("D"),
            AuthorName = commit.AuthorName,
            AuthorEmail = commit.AuthorEmail,
            AuthoredAt = commit.AuthoredAt.ToString("O", CultureInfo.InvariantCulture),
            Subject = commit.Subject
        };
    }
}
