namespace WeeklyReport.Application;

public enum GitAuthorField
{
    Name,
    Email
}

public sealed class GitRepositoryQuery
{
    public GitRepositoryQuery(
        Guid projectId,
        string repositoryPath,
        GitAuthorField authorField,
        string author,
        DateOnly start,
        DateOnly end,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var source = GitSourceValidation.Validate(projectId, repositoryPath, authorField, author);

        if (start > end)
        {
            throw new ArgumentException("开始日期不能晚于结束日期。", nameof(start));
        }

        ProjectId = projectId;
        RepositoryPath = source.Path;
        AuthorField = authorField;
        Author = source.Author;
        Start = start;
        End = end;
        TimeZone = timeZone;
    }

    public Guid ProjectId { get; }
    public string RepositoryPath { get; }
    public GitAuthorField AuthorField { get; }
    public string Author { get; }
    public DateOnly Start { get; }
    public DateOnly End { get; }
    public TimeZoneInfo TimeZone { get; }
}
