using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public class GitHourEstimatorTests
{
    private static readonly Guid ProjectA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ProjectB = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid ProjectC = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private readonly GitHourEstimator _estimator = new();

    [Fact]
    public void SingleCommit_HasUnknownDurationRatherThanZero()
    {
        var result = Estimate(Commit(ProjectA, 1, At(9, 0)));

        var day = Assert.Single(result);
        Assert.Null(day.TotalMinutes);
        var project = Assert.Single(day.Projects);
        Assert.Null(project.Minutes);
        Assert.Equal(1, project.CommitCount);
    }

    [Fact]
    public void GapBoundary_IncludesTwoHoursButExcludesLongerGap()
    {
        var result = Estimate(
            Commit(ProjectA, 1, At(9, 0)),
            Commit(ProjectA, 2, At(11, 0)),
            Commit(ProjectA, 3, At(13, 0).AddSeconds(1)));

        var day = Assert.Single(result);
        Assert.Equal(120, day.TotalMinutes);
        Assert.Equal(120, Assert.Single(day.Projects).Minutes);
    }

    [Fact]
    public void PartialOverlap_IsSharedEquallyWithoutDoubleCounting()
    {
        var result = Estimate(
            Commit(ProjectA, 1, At(9, 0)),
            Commit(ProjectA, 2, At(10, 0)),
            Commit(ProjectB, 3, At(9, 30)),
            Commit(ProjectB, 4, At(10, 30)));

        var day = Assert.Single(result);
        Assert.Equal(90, day.TotalMinutes);
        Assert.Equal(45, Minutes(day, ProjectA));
        Assert.Equal(45, Minutes(day, ProjectB));
    }

    [Fact]
    public void ThreeOverlappingProjects_ShareTheSameHour()
    {
        var result = Estimate(
            Commit(ProjectA, 1, At(9, 0)), Commit(ProjectA, 2, At(10, 0)),
            Commit(ProjectB, 3, At(9, 0)), Commit(ProjectB, 4, At(10, 0)),
            Commit(ProjectC, 5, At(9, 0)), Commit(ProjectC, 6, At(10, 0)));

        var day = Assert.Single(result);
        Assert.Equal(60, day.TotalMinutes);
        Assert.Equal(20, Minutes(day, ProjectA));
        Assert.Equal(20, Minutes(day, ProjectB));
        Assert.Equal(20, Minutes(day, ProjectC));
    }

    [Fact]
    public void MoreThanEightHours_IsCappedAndAllocatedProportionally()
    {
        var commits = Enumerable.Range(0, 6)
            .SelectMany(index => new[]
            {
                Commit(ProjectA, index + 1, At(8 + index * 2, 0)),
                Commit(ProjectB, index + 11, At(8 + index * 2, 0))
            });

        var day = Assert.Single(_estimator.Estimate(commits, TimeZoneInfo.Utc));

        Assert.Equal(480, day.TotalMinutes);
        Assert.Equal(240, Minutes(day, ProjectA));
        Assert.Equal(240, Minutes(day, ProjectB));
        Assert.Equal(day.TotalMinutes, day.Projects.Sum(project => project.Minutes ?? 0));
    }

    [Fact]
    public void FractionalMinutes_RoundDeterministicallyAndPreserveDailyTotal()
    {
        var commits = new[]
        {
            Commit(ProjectC, 1, At(9, 0)), Commit(ProjectC, 2, At(9, 1)),
            Commit(ProjectB, 3, At(9, 0)), Commit(ProjectB, 4, At(9, 1)),
            Commit(ProjectA, 5, At(9, 0)), Commit(ProjectA, 6, At(9, 1))
        };

        var day = Assert.Single(_estimator.Estimate(commits, TimeZoneInfo.Utc));

        Assert.Equal(1, day.TotalMinutes);
        Assert.Equal(1, Minutes(day, ProjectA));
        Assert.Equal(0, Minutes(day, ProjectB));
        Assert.Equal(0, Minutes(day, ProjectC));
        Assert.Equal(day.TotalMinutes, day.Projects.Sum(project => project.Minutes ?? 0));
    }

    [Fact]
    public void AuthorTimes_AreGroupedBySystemLocalDateWithoutCrossingMidnight()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("UTC+08", TimeSpan.FromHours(8), "UTC+08", "UTC+08");
        var first = new DateTimeOffset(2026, 10, 30, 15, 30, 0, TimeSpan.Zero);
        var second = first.AddHours(1);

        var days = _estimator.Estimate(
            [Commit(ProjectA, 1, first), Commit(ProjectA, 2, second)],
            timeZone);

        Assert.Equal(2, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 30), days[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 31), days[1].Date);
        Assert.All(days, day => Assert.Null(day.TotalMinutes));
    }

    [Fact]
    public void DaylightSavingFallback_UsesElapsedTimeRatherThanWallClockLabels()
    {
        var transitionStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var transitionEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
            transitionStart, transitionEnd);
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Test Eastern", TimeSpan.FromHours(-5), "Test Eastern", "Standard", "Daylight", [rule]);
        var first = new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero);
        var second = first.AddHours(1);

        var day = Assert.Single(_estimator.Estimate(
            [Commit(ProjectA, 1, first), Commit(ProjectA, 2, second)], timeZone));

        Assert.Equal(new DateOnly(2026, 11, 1), day.Date);
        Assert.Equal(60, day.TotalMinutes);
    }

    [Fact]
    public void DuplicateCommit_IsCountedOnce()
    {
        var first = Commit(ProjectA, 1, At(9, 0));
        var second = Commit(ProjectA, 2, At(10, 0));

        var day = Assert.Single(Estimate(first, first, second, second));

        Assert.Equal(60, day.TotalMinutes);
        Assert.Equal(2, Assert.Single(day.Projects).CommitCount);
    }

    [Fact]
    public void DuplicateHashWithDifferentContent_IsRejected()
    {
        var first = Commit(ProjectA, 1, At(9, 0));
        var conflicting = new GitCommit(ProjectA, first.Hash, "name", "person@example.invalid", At(10, 0), "different");

        Assert.Throws<ArgumentException>(() => Estimate(first, conflicting));
    }

    [Fact]
    public void EstimateDated_PreservesPreviouslyAssignedLocalDates()
    {
        var storedDate = new DateOnly(2026, 10, 30);
        var first = new DateTimeOffset(2026, 10, 30, 23, 30, 0, TimeSpan.Zero);
        var result = _estimator.EstimateDated(
        [
            new DatedGitCommit(Commit(ProjectA, 1, first), storedDate),
            new DatedGitCommit(Commit(ProjectA, 2, first.AddHours(1)), storedDate)
        ]);

        var day = Assert.Single(result);
        Assert.Equal(storedDate, day.Date);
        Assert.Equal(60, day.TotalMinutes);
    }

    [Fact]
    public void EstimateDated_RejectsConflictingDatesForTheSameCommit()
    {
        var commit = Commit(ProjectA, 1, At(9, 0));

        Assert.Throws<ArgumentException>(() => _estimator.EstimateDated(
        [
            new DatedGitCommit(commit, new DateOnly(2026, 10, 30)),
            new DatedGitCommit(commit, new DateOnly(2026, 10, 31))
        ]));
    }

    [Fact]
    public void UnknownProject_IsDistinguishedFromAProjectWithAnInterval()
    {
        var day = Assert.Single(Estimate(
            Commit(ProjectA, 1, At(9, 0)),
            Commit(ProjectA, 2, At(10, 0)),
            Commit(ProjectB, 3, At(9, 30))));

        Assert.Equal(60, day.TotalMinutes);
        Assert.Equal(60, Minutes(day, ProjectA));
        Assert.Null(Minutes(day, ProjectB));
    }

    [Fact]
    public void SameTimestamp_DoesNotInventAnInterval()
    {
        var day = Assert.Single(Estimate(
            Commit(ProjectA, 1, At(9, 0)),
            Commit(ProjectA, 2, At(9, 0))));

        Assert.Null(day.TotalMinutes);
        Assert.Null(Minutes(day, ProjectA));
    }

    [Fact]
    public void InvalidHashOrProject_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new GitCommit(ProjectA, "short", "name", "person@example.invalid", At(9, 0), "work"));
        Assert.Throws<ArgumentException>(
            () => new GitCommit(Guid.Empty, Hash(1), "name", "person@example.invalid", At(9, 0), "work"));
    }

    [Fact]
    public void RandomOverlaps_RemainDeterministicAndRespectTheDailyLimit()
    {
        var random = new Random(12345);
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var commits = new List<GitCommit>();
            var nextHash = 1;
            foreach (var project in new[] { ProjectA, ProjectB, ProjectC })
            {
                for (var index = 0; index < 12; index++)
                {
                    var minute = random.Next(0, 24 * 60);
                    commits.Add(Commit(project, nextHash++, At(0, 0).AddMinutes(minute)));
                }
            }

            var first = Assert.Single(_estimator.Estimate(commits, TimeZoneInfo.Utc));
            var second = Assert.Single(_estimator.Estimate(commits.AsEnumerable().Reverse(), TimeZoneInfo.Utc));

            Assert.InRange(first.TotalMinutes!.Value, 0, 480);
            Assert.Equal(first.TotalMinutes, first.Projects.Sum(project => project.Minutes ?? 0));
            Assert.Equal(
                first.Projects.Select(project => (project.ProjectId, project.Minutes)),
                second.Projects.Select(project => (project.ProjectId, project.Minutes)));
        }
    }

    private IReadOnlyList<DailyGitHourEstimate> Estimate(params GitCommit[] commits) =>
        _estimator.Estimate(commits, TimeZoneInfo.Utc);

    private static int? Minutes(DailyGitHourEstimate day, Guid project) =>
        Assert.Single(day.Projects, item => item.ProjectId == project).Minutes;

    private static GitCommit Commit(Guid project, int hash, DateTimeOffset time) =>
        new(project, Hash(hash), "name", "person@example.invalid", time, "work");

    private static string Hash(int value) => value.ToString("x").PadLeft(40, '0');

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 10, 30, hour, minute, 0, TimeSpan.Zero);
}
