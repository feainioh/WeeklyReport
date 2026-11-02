using WeeklyReport.Application;

namespace WeeklyReport.UnitTests;

public class BackupValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProjectId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid GitItemId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid ManualId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly DateOnly FirstWeek = new(2026, 10, 26);
    private static readonly DateOnly SecondWeek = new(2026, 11, 2);

    [Fact]
    public void SelectWeeks_ExportsOnlySelectedWeekAndDeduplicatesSelection()
    {
        var source = ValidBackup();

        var selected = BackupSelection.SelectWeeks(source, [FirstWeek, FirstWeek]);

        Assert.Single(selected.Reports);
        Assert.Equal(FirstWeek, selected.Reports[0].WeekStart);
        Assert.Equal(2, selected.Entries.Count);
        Assert.Single(selected.Projects);
        Assert.Single(selected.Commits);
        Assert.DoesNotContain(selected.Entries, entry => entry.WorkDate == SecondWeek);
    }

    [Fact]
    public void SelectWeeks_DoesNotDeduplicateDifferentWeeksByDescription()
    {
        var source = ValidBackup();

        var selected = BackupSelection.SelectWeeks(source, [FirstWeek, SecondWeek]);

        Assert.Equal(2, selected.Reports.Count);
        Assert.Equal(2, selected.Entries.Count(entry => entry.Description == "相同内容"));
    }

    [Fact]
    public void SelectWeeks_RejectsMissingWeeks()
    {
        Assert.Throws<ArgumentException>(() => BackupSelection.SelectWeeks(ValidBackup(), [new DateOnly(2026, 11, 9)]));
    }

    [Fact]
    public void Validate_RejectsDuplicateWorkItemIds()
    {
        var source = ValidBackup();
        var duplicate = source with { Entries = [source.Entries[0], source.Entries[0]] };

        Assert.Throws<BackupFormatException>(() => BackupValidator.Validate(duplicate));
    }

    [Fact]
    public void Validate_RejectsMismatchedGitProjectAndWorkItem()
    {
        var source = ValidBackup();
        var invalid = source with { Commits = [source.Commits[0] with { WorkItemId = ManualId }] };

        Assert.Throws<BackupFormatException>(() => BackupValidator.Validate(invalid));
    }

    [Fact]
    public void Validate_RejectsUnsupportedVersionAndInvalidWeek()
    {
        var source = ValidBackup();
        Assert.Throws<BackupFormatException>(() => BackupValidator.Validate(source with { FormatVersion = 2 }));
        Assert.Throws<BackupFormatException>(() =>
            BackupValidator.Validate(source with { Reports = [source.Reports[0] with { WeekEnd = FirstWeek }] }));
    }

    [Fact]
    public void Validate_RequiresDeletedGitContentToBeRedacted()
    {
        var source = ValidBackup();
        var deleted = source.Entries[0] with
        {
            Description = "（已删除）",
            IsDeleted = true,
            EstimatedMinutes = null,
            ConfirmedMinutes = null
        };
        var invalid = source with { Entries = [deleted, source.Entries[1], source.Entries[2]] };
        Assert.Throws<BackupFormatException>(() => BackupValidator.Validate(invalid));

        var redacted = source.Commits[0] with { AuthorName = "", AuthorEmail = "", Subject = "" };
        BackupValidator.Validate(invalid with { Commits = [redacted] });
    }

    [Fact]
    public void Validate_RejectsInvalidUnicodeWithoutTruncating()
    {
        var source = ValidBackup();
        var invalid = source with { Entries = [source.Entries[0] with { Description = "\ud800" }] };

        Assert.Throws<BackupFormatException>(() => BackupValidator.Validate(invalid));
    }

    internal static BackupDocument ValidBackup()
    {
        var git = new BackupEntry(GitItemId, BackupEntryKind.Git, ProjectId, FirstWeek,
            "Git 工作", 45, null, false, null, null);
        var firstManual = new BackupEntry(ManualId, BackupEntryKind.Manual, null, FirstWeek.AddDays(1),
            "相同内容", null, 60, false, Now, Now);
        var secondManual = new BackupEntry(Guid.NewGuid(), BackupEntryKind.Manual, null, SecondWeek,
            "相同内容", null, 30, false, Now, Now);
        var commit = new BackupCommit(ProjectId, GitItemId, new string('a', 40),
            "name", "test@example.invalid", Now, "实现功能");
        var first = Report(FirstWeek);
        var second = Report(SecondWeek);
        return new BackupDocument(1, Now, [new BackupProject(ProjectId, "项目")],
            [git, firstManual, secondManual], [commit], [first, second]);
    }

    private static BackupReport Report(DateOnly start) =>
        new(Guid.NewGuid(), start, start.AddDays(6), "本周工作", "下周计划", "风险", 60, 45, 0, Now, Now);
}
