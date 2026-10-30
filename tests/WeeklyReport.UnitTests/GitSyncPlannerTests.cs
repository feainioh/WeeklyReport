using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.UnitTests;

public class GitSyncPlannerTests
{
    private static readonly Guid ProjectA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ProjectB = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly DateOnly Day = new(2026, 10, 30);
    private readonly GitSyncPlanner _planner = new(new GitHourEstimator());

    [Fact]
    public void Preview_GroupsByProjectAndDayAndSplitsOverlappingHours()
    {
        var preview = _planner.CreatePreview(
            [],
            [
                Commit(ProjectA, 1, 9, 0, "实现功能"),
                Commit(ProjectA, 2, 10, 0, "修复问题"),
                Commit(ProjectB, 3, 9, 30, "更新文档"),
                Commit(ProjectB, 4, 10, 30, "补充测试")
            ],
            TimeZoneInfo.Utc);

        Assert.Equal(2, preview.Proposals.Count);
        Assert.All(preview.Proposals, proposal => Assert.Null(proposal.CurrentDescription));
        Assert.Contains("实现功能", preview.Proposals.Single(item => item.ProjectId == ProjectA).SuggestedDescription);
        var day = Assert.Single(preview.SuggestedHours);
        Assert.Equal(90, day.TotalMinutes);
        Assert.Equal(45, day.Projects.Single(item => item.ProjectId == ProjectA).Minutes);
        Assert.Equal(45, day.Projects.Single(item => item.ProjectId == ProjectB).Minutes);
    }

    [Fact]
    public void Preview_PreservesManualDescriptionAndConfirmedHours()
    {
        var first = Commit(ProjectA, 1, 9, 0, "原提交");
        var item = new GitWorkItemSnapshot(Guid.NewGuid(), ProjectA, Day, "人工编辑的描述", 90, false, [first]);
        var added = Commit(ProjectA, 2, 10, 0, "新提交");

        var preview = _planner.CreatePreview([item], [first, added, added], TimeZoneInfo.Utc);

        var proposal = Assert.Single(preview.Proposals);
        Assert.Equal(item.Id, proposal.WorkItemId);
        Assert.Equal("人工编辑的描述", proposal.CurrentDescription);
        Assert.Equal(90, proposal.ConfirmedMinutes);
        Assert.Single(proposal.AddedCommits);
        Assert.Contains("原提交", proposal.SuggestedDescription);
        Assert.Contains("新提交", proposal.SuggestedDescription);
        Assert.Equal(2, preview.DuplicateCount);
        Assert.Equal(60, Assert.Single(preview.SuggestedHours).TotalMinutes);
        Assert.Equal("人工编辑的描述", item.Description);
        Assert.Equal(90, item.ConfirmedMinutes);
    }

    [Fact]
    public void Preview_IncludesAffectedOtherProjectInHourSuggestions()
    {
        var other = new GitWorkItemSnapshot(
            Guid.NewGuid(), ProjectB, Day, "已确认工作", 30, false,
            [Commit(ProjectB, 3, 9, 30, "B1"), Commit(ProjectB, 4, 10, 30, "B2")]);

        var preview = _planner.CreatePreview(
            [other], [Commit(ProjectA, 1, 9, 0, "A1"), Commit(ProjectA, 2, 10, 0, "A2")],
            TimeZoneInfo.Utc);

        Assert.Single(preview.Proposals);
        var day = Assert.Single(preview.SuggestedHours);
        Assert.Equal(45, day.Projects.Single(item => item.ProjectId == ProjectA).Minutes);
        Assert.Equal(45, day.Projects.Single(item => item.ProjectId == ProjectB).Minutes);
        Assert.Equal(30, other.ConfirmedMinutes);
    }

    [Fact]
    public void Preview_DoesNotSilentlyRestoreDeletedItems()
    {
        var old = Commit(ProjectA, 1, 9, 0, "已删除");
        var deleted = new GitWorkItemSnapshot(Guid.NewGuid(), ProjectA, Day, "已删除工作", 60, true, [old]);

        var preview = _planner.CreatePreview(
            [deleted], [old, Commit(ProjectA, 2, 10, 0, "新增提交")], TimeZoneInfo.Utc);

        Assert.Empty(preview.Proposals);
        Assert.Empty(preview.SuggestedHours);
        Assert.Equal(1, preview.DuplicateCount);
        var conflict = Assert.Single(preview.DeletedConflicts);
        Assert.Equal(deleted.Id, conflict.WorkItemId);
        Assert.Single(conflict.NewCommits);
    }

    [Fact]
    public void Preview_RecognizesRedactedDeletedCommitsByIdentity()
    {
        var original = Commit(ProjectA, 1, 9, 0, "原始摘要");
        var redacted = new GitCommit(ProjectA, original.Hash, "", "", original.AuthoredAt, "");
        var deleted = new GitWorkItemSnapshot(Guid.NewGuid(), ProjectA, Day, "（已删除）", null, true, [redacted]);

        var preview = _planner.CreatePreview([deleted], [original], TimeZoneInfo.Utc);

        Assert.Empty(preview.Proposals);
        Assert.Empty(preview.DeletedConflicts);
        Assert.Equal(1, preview.DuplicateCount);
    }

    [Fact]
    public void Preview_RejectsConflictingMetadataForTheSameHash()
    {
        var original = Commit(ProjectA, 1, 9, 0, "原内容");
        var changed = new GitCommit(ProjectA, original.Hash, "name", "person@example.invalid", original.AuthoredAt, "不同内容");
        var item = new GitWorkItemSnapshot(Guid.NewGuid(), ProjectA, Day, "已保存", null, false, [original]);

        Assert.Throws<ArgumentException>(() => _planner.CreatePreview([item], [changed], TimeZoneInfo.Utc));
    }

    [Fact]
    public void Preview_PreservesStoredWorkDateAfterTimeZoneChanges()
    {
        var first = new GitCommit(ProjectA, Hash(1), "name", "person@example.invalid",
            new DateTimeOffset(2026, 10, 30, 23, 0, 0, TimeSpan.Zero), "第一项");
        var second = new GitCommit(ProjectA, Hash(2), "name", "person@example.invalid",
            first.AuthoredAt.AddMinutes(30), "第二项");
        var existing = new GitWorkItemSnapshot(Guid.NewGuid(), ProjectA, Day, "已保存", null, false, [first]);
        var currentZone = TimeZoneInfo.CreateCustomTimeZone("UTC+02", TimeSpan.FromHours(2), "UTC+02", "UTC+02");

        var preview = _planner.CreatePreview([existing], [second], currentZone);

        Assert.Equal(Day, existing.WorkDate);
        Assert.Equal(new DateOnly(2026, 10, 31), Assert.Single(preview.Proposals).WorkDate);
        Assert.Null(Assert.Single(preview.SuggestedHours).TotalMinutes);
    }

    [Fact]
    public void Preview_EmptySubjectIsMadeExplicitWithoutInventingContent()
    {
        var preview = _planner.CreatePreview(
            [], [Commit(ProjectA, 1, 9, 0, string.Empty)], TimeZoneInfo.Utc);

        Assert.Contains("无提交摘要", Assert.Single(preview.Proposals).SuggestedDescription);
        Assert.Null(Assert.Single(preview.SuggestedHours).TotalMinutes);
    }

    [Fact]
    public void Preview_RejectsDuplicateProjectDaySnapshots()
    {
        var first = new GitWorkItemSnapshot(Guid.NewGuid(), ProjectA, Day, "第一条", null, false, []);
        var second = new GitWorkItemSnapshot(Guid.NewGuid(), ProjectA, Day, "第二条", null, false, []);

        Assert.Throws<ArgumentException>(() => _planner.CreatePreview([first, second], [], TimeZoneInfo.Utc));
    }

    private static GitCommit Commit(Guid project, int hash, int hour, int minute, string subject) =>
        new(project, Hash(hash), "name", "person@example.invalid",
            new DateTimeOffset(2026, 10, 30, hour, minute, 0, TimeSpan.Zero), subject);

    private static string Hash(int value) => value.ToString("x").PadLeft(40, '0');
}
