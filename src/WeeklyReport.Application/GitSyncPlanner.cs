using WeeklyReport.Domain;

namespace WeeklyReport.Application;

public sealed class GitSyncPlanner(GitHourEstimator estimator)
{
    public GitSyncPreview CreatePreview(
        IEnumerable<GitWorkItemSnapshot> existingItems,
        IEnumerable<GitCommit> incomingCommits,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(existingItems);
        ArgumentNullException.ThrowIfNull(incomingCommits);
        ArgumentNullException.ThrowIfNull(timeZone);

        var itemsByDay = new Dictionary<(Guid ProjectId, DateOnly Date), GitWorkItemSnapshot>();
        var known = new Dictionary<(Guid ProjectId, string Hash), (GitCommit Commit, bool IsDeleted)>();
        var dated = new List<DatedGitCommit>();
        foreach (var item in existingItems)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!itemsByDay.TryAdd((item.ProjectId, item.WorkDate), item))
            {
                throw new ArgumentException("同一项目和日期存在重复的 Git 工作项。", nameof(existingItems));
            }

            foreach (var commit in item.Commits)
            {
                var key = (commit.ProjectId, commit.Hash);
                if (!known.TryAdd(key, (commit, item.IsDeleted)))
                {
                    throw new ArgumentException("已有 Git 工作项包含重复的提交。", nameof(existingItems));
                }

                if (!item.IsDeleted)
                {
                    dated.Add(new DatedGitCommit(commit, item.WorkDate));
                }
            }
        }

        var incoming = new Dictionary<(Guid ProjectId, string Hash), GitCommit>();
        var duplicateCount = 0;
        foreach (var commit in incomingCommits)
        {
            ArgumentNullException.ThrowIfNull(commit);
            var key = (commit.ProjectId, commit.Hash);
            if (known.TryGetValue(key, out var saved))
            {
                if (!saved.IsDeleted && !saved.Commit.HasSameMetadata(commit))
                {
                    throw new ArgumentException("相同项目和提交哈希对应的数据不一致。", nameof(incomingCommits));
                }

                duplicateCount++;
                continue;
            }

            if (incoming.TryGetValue(key, out var prior))
            {
                if (!prior.HasSameMetadata(commit))
                {
                    throw new ArgumentException("相同项目和提交哈希对应的数据不一致。", nameof(incomingCommits));
                }

                duplicateCount++;
                continue;
            }

            incoming.Add(key, commit);
        }

        var proposals = new List<GitWorkItemProposal>();
        var deletedConflicts = new List<DeletedGitWorkItemConflict>();
        var affectedDates = new HashSet<DateOnly>();
        var groups = incoming.Values
            .Select(commit => new DatedGitCommit(
                commit,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(commit.AuthoredAt, timeZone).DateTime)))
            .GroupBy(item => (item.Commit.ProjectId, item.WorkDate))
            .OrderBy(group => group.Key.WorkDate)
            .ThenBy(group => group.Key.ProjectId);

        foreach (var group in groups)
        {
            StorageDateRange.ValidateWorkDate(group.Key.WorkDate);
            itemsByDay.TryGetValue((group.Key.ProjectId, group.Key.WorkDate), out var existing);
            var added = group.Select(item => item.Commit)
                .OrderBy(commit => commit.AuthoredAt.UtcDateTime)
                .ThenBy(commit => commit.Hash, StringComparer.Ordinal)
                .ToArray();
            if (existing is { IsDeleted: true })
            {
                deletedConflicts.Add(new DeletedGitWorkItemConflict(
                    existing.Id, existing.ProjectId, existing.WorkDate, Array.AsReadOnly(added)));
                continue;
            }

            affectedDates.Add(group.Key.WorkDate);
            dated.AddRange(group);
            var allCommits = (existing?.Commits ?? []).Concat(added);
            proposals.Add(new GitWorkItemProposal(
                existing?.Id ?? Guid.NewGuid(),
                group.Key.ProjectId,
                group.Key.WorkDate,
                existing?.Description,
                Describe(allCommits),
                existing?.ConfirmedMinutes,
                Array.AsReadOnly(added)));
        }

        var hours = estimator.EstimateDated(dated.Where(item => affectedDates.Contains(item.WorkDate))).ToArray();
        return new GitSyncPreview(
            proposals.AsReadOnly(), deletedConflicts.AsReadOnly(), Array.AsReadOnly(hours), duplicateCount);
    }

    private static string Describe(IEnumerable<GitCommit> commits) => string.Join(
        Environment.NewLine,
        commits.OrderBy(commit => commit.AuthoredAt.UtcDateTime)
            .ThenBy(commit => commit.Hash, StringComparer.Ordinal)
            .Select(commit =>
            {
                var subject = string.IsNullOrWhiteSpace(commit.Subject)
                    ? "（无提交摘要）"
                    : commit.Subject.Trim().Replace('\r', ' ').Replace('\n', ' ');
                return $"- {subject} ({commit.Hash[..8]})";
            }));
}
