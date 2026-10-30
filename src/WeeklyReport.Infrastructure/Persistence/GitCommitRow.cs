using SqlSugar;

namespace WeeklyReport.Infrastructure.Persistence;

[SugarTable("git_commit_links")]
public sealed class GitCommitRow
{
    [SugarColumn(ColumnName = "project_id", IsPrimaryKey = true)]
    public string ProjectId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "commit_hash", IsPrimaryKey = true)]
    public string CommitHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "work_item_id")]
    public string WorkItemId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "author_name")]
    public string AuthorName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "author_email")]
    public string AuthorEmail { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "authored_at")]
    public string AuthoredAt { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "subject")]
    public string Subject { get; set; } = string.Empty;
}
