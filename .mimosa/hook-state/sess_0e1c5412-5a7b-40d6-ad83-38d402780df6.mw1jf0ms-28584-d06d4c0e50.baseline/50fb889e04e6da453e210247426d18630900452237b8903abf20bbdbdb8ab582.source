using WeeklyReport.Application;

namespace WeeklyReport.UnitTests;

public class BackupImportPlannerTests
{
    [Fact]
    public void Prepare_CountsExistingRecordsWithoutModifyingThem()
    {
        var current = BackupValidationTests.ValidBackup();
        var mutableProjects = current.Projects.ToList();
        var incoming = current with { Projects = mutableProjects };

        var plan = new BackupImportPlanner().Prepare(incoming, current);
        mutableProjects.Clear();

        Assert.Single(plan.Incoming.Projects);
        Assert.Equal(1, plan.Conflicts.Projects);
        Assert.Equal(2, plan.Conflicts.ManualEntries);
        Assert.Equal(1, plan.Conflicts.GitWorkItems);
        Assert.Equal(1, plan.Conflicts.GitCommits);
        Assert.Equal(2, plan.Conflicts.Reports);
    }

    [Fact]
    public void Prepare_RejectsAnIdentifierReusedForAnotherWeek()
    {
        var current = BackupValidationTests.ValidBackup();
        var report = current.Reports[0];
        var differentWeek = report with { WeekStart = new DateOnly(2026, 11, 9), WeekEnd = new DateOnly(2026, 11, 15) };
        var incoming = current with { Reports = [differentWeek] };

        Assert.Throws<BackupFormatException>(() => new BackupImportPlanner().Prepare(incoming, current));
    }

    [Fact]
    public void Prepare_RejectsACommitAttachedToAnotherDay()
    {
        var current = BackupValidationTests.ValidBackup();
        var git = current.Entries.Single(entry => entry.Kind == BackupEntryKind.Git);
        var incoming = current with
        {
            Entries = [git with { Id = Guid.NewGuid(), WorkDate = git.WorkDate.AddDays(1) }],
            Commits = [current.Commits[0] with { WorkItemId = Guid.Empty }],
            Reports = []
        };
        incoming = incoming with { Commits = [incoming.Commits[0] with { WorkItemId = incoming.Entries[0].Id }] };

        Assert.Throws<BackupFormatException>(() => new BackupImportPlanner().Prepare(incoming, current));
    }

    [Fact]
    public void Prepare_ReportsExplicitRestorationOfDeletedGitWorkItems()
    {
        var incoming = BackupValidationTests.ValidBackup();
        var git = incoming.Entries.Single(entry => entry.Kind == BackupEntryKind.Git);
        var deleted = git with { Description = "（已删除）", IsDeleted = true, EstimatedMinutes = null, ConfirmedMinutes = null };
        var current = incoming with
        {
            Entries = incoming.Entries.Select(entry => entry.Id == git.Id ? deleted : entry).ToArray(),
            Commits = [incoming.Commits[0] with { AuthorName = "", AuthorEmail = "", Subject = "" }]
        };

        var plan = new BackupImportPlanner().Prepare(incoming, current);

        Assert.Equal(1, plan.Conflicts.RestoredGitWorkItems);
        Assert.Equal(1, plan.Conflicts.GitWorkItems);
    }
}
