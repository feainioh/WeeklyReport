using SqlSugar;

namespace WeeklyReport.Infrastructure.Persistence;

[SugarTable("git_projects")]
public sealed class GitProjectRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "repository_path", IsNullable = true)]
    public string? RepositoryPath { get; set; }

    [SugarColumn(ColumnName = "author_field", IsNullable = true)]
    public string? AuthorField { get; set; }

    [SugarColumn(ColumnName = "author_value", IsNullable = true)]
    public string? Author { get; set; }
}
