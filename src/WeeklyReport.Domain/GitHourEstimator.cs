namespace WeeklyReport.Domain;

public sealed record ProjectHourEstimate(Guid ProjectId, int? Minutes, int CommitCount);

public sealed record DailyGitHourEstimate(
    DateOnly Date,
    int? TotalMinutes,
    IReadOnlyList<ProjectHourEstimate> Projects);

public sealed class GitHourEstimator
{
    private static readonly TimeSpan MaximumGap = TimeSpan.FromHours(2);
    private static readonly long DailyLimitTicks = TimeSpan.FromHours(8).Ticks;

    public IReadOnlyList<DailyGitHourEstimate> Estimate(
        IEnumerable<GitCommit> commits,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(timeZone);

        return EstimateDated(commits.Select(commit =>
        {
            ArgumentNullException.ThrowIfNull(commit);
            return new DatedGitCommit(
                commit,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(commit.AuthoredAt, timeZone).DateTime));
        }));
    }

    public IReadOnlyList<DailyGitHourEstimate> EstimateDated(IEnumerable<DatedGitCommit> commits)
    {
        ArgumentNullException.ThrowIfNull(commits);
        return Deduplicate(commits)
            .GroupBy(item => item.WorkDate)
            .OrderBy(group => group.Key)
            .Select(EstimateDay)
            .ToArray();
    }

    private static IReadOnlyCollection<DatedGitCommit> Deduplicate(IEnumerable<DatedGitCommit> commits)
    {
        var unique = new Dictionary<(Guid ProjectId, string Hash), DatedGitCommit>();
        foreach (var dated in commits)
        {
            ArgumentNullException.ThrowIfNull(dated);
            var commit = dated.Commit;
            var key = (commit.ProjectId, commit.Hash);
            if (unique.TryGetValue(key, out var existing))
            {
                if (existing.WorkDate != dated.WorkDate || !existing.Commit.HasSameMetadata(commit))
                {
                    throw new ArgumentException("相同项目和提交哈希对应的数据不一致。", nameof(commits));
                }

                continue;
            }

            unique.Add(key, dated);
        }

        return unique.Values;
    }

    private static DailyGitHourEstimate EstimateDay(IGrouping<DateOnly, DatedGitCommit> day)
    {
        var projectGroups = day.GroupBy(item => item.Commit.ProjectId)
            .OrderBy(group => group.Key)
            .ToArray();
        var events = new List<IntervalEvent>();
        var projectsWithIntervals = new HashSet<Guid>();

        foreach (var project in projectGroups)
        {
            var ordered = project.Select(item => item.Commit)
                .OrderBy(commit => commit.AuthoredAt.UtcDateTime)
                .ThenBy(commit => commit.Hash, StringComparer.Ordinal)
                .ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = ordered[index - 1].AuthoredAt.UtcDateTime;
                var current = ordered[index].AuthoredAt.UtcDateTime;
                var gap = current - previous;
                if (gap <= TimeSpan.Zero || gap > MaximumGap)
                {
                    continue;
                }

                events.Add(new IntervalEvent(previous.Ticks, project.Key, 1));
                events.Add(new IntervalEvent(current.Ticks, project.Key, -1));
                projectsWithIntervals.Add(project.Key);
            }
        }

        if (events.Count == 0)
        {
            return new DailyGitHourEstimate(
                day.Key,
                null,
                projectGroups.Select(group => new ProjectHourEstimate(group.Key, null, group.Count())).ToArray());
        }

        var allocatedTicks = projectGroups.ToDictionary(group => group.Key, _ => 0m);
        var active = new Dictionary<Guid, int>();
        long totalTicks = 0;
        long? previousTick = null;
        foreach (var boundary in events.OrderBy(item => item.Tick).GroupBy(item => item.Tick))
        {
            if (previousTick is long lastTick && active.Count > 0)
            {
                var duration = boundary.Key - lastTick;
                totalTicks += duration;
                var share = (decimal)duration / active.Count;
                foreach (var projectId in active.Keys)
                {
                    allocatedTicks[projectId] += share;
                }
            }

            foreach (var item in boundary)
            {
                var count = active.GetValueOrDefault(item.ProjectId) + item.Delta;
                if (count == 0)
                {
                    active.Remove(item.ProjectId);
                }
                else
                {
                    active[item.ProjectId] = count;
                }
            }

            previousTick = boundary.Key;
        }

        var cappedTicks = Math.Min(totalTicks, DailyLimitTicks);
        var totalMinutes = (int)decimal.Round(
            (decimal)cappedTicks / TimeSpan.TicksPerMinute, 0, MidpointRounding.AwayFromZero);
        var scale = (decimal)cappedTicks / totalTicks;
        var allocations = projectGroups
            .Where(group => projectsWithIntervals.Contains(group.Key))
            .Select(group => CreateAllocation(group.Key, allocatedTicks[group.Key] * scale))
            .ToArray();
        var remaining = totalMinutes - allocations.Sum(item => item.Minutes);
        if (remaining < 0 || remaining > allocations.Length)
        {
            throw new InvalidOperationException("工时舍入结果不一致。");
        }

        foreach (var allocation in allocations
            .OrderByDescending(item => item.Fraction)
            .ThenBy(item => item.ProjectId)
            .Take(remaining))
        {
            allocation.Minutes++;
        }

        var minutesByProject = allocations.ToDictionary(item => item.ProjectId, item => item.Minutes);
        return new DailyGitHourEstimate(
            day.Key,
            totalMinutes,
            projectGroups.Select(group => new ProjectHourEstimate(
                group.Key,
                minutesByProject.TryGetValue(group.Key, out var minutes) ? minutes : null,
                group.Count())).ToArray());
    }

    private static Allocation CreateAllocation(Guid projectId, decimal ticks)
    {
        var exactMinutes = ticks / TimeSpan.TicksPerMinute;
        var wholeMinutes = (int)decimal.Floor(exactMinutes);
        return new Allocation(projectId, wholeMinutes, exactMinutes - wholeMinutes);
    }

    private sealed record IntervalEvent(long Tick, Guid ProjectId, int Delta);

    private sealed class Allocation(Guid projectId, int minutes, decimal fraction)
    {
        public Guid ProjectId { get; } = projectId;
        public int Minutes { get; set; } = minutes;
        public decimal Fraction { get; } = fraction;
    }
}
