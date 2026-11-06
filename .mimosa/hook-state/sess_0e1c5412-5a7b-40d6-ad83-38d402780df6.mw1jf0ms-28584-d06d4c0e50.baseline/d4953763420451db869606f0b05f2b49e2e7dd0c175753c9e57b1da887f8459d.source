namespace WeeklyReport.Domain;

public sealed class GitCommit
{
    public GitCommit(
        Guid projectId,
        string hash,
        string authorName,
        string authorEmail,
        DateTimeOffset authoredAt,
        string subject)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("项目标识不能为空。", nameof(projectId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentNullException.ThrowIfNull(authorName);
        ArgumentNullException.ThrowIfNull(authorEmail);
        ArgumentNullException.ThrowIfNull(subject);
        if (hash.Length is not (40 or 64) || !hash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("提交标识必须是完整的 Git 哈希。", nameof(hash));
        }

        ProjectId = projectId;
        Hash = hash.ToLowerInvariant();
        AuthorName = authorName;
        AuthorEmail = authorEmail;
        AuthoredAt = authoredAt;
        Subject = subject;
    }

    public Guid ProjectId { get; }
    public string Hash { get; }
    public string AuthorName { get; }
    public string AuthorEmail { get; }
    public DateTimeOffset AuthoredAt { get; }
    public string Subject { get; }

    public bool HasSameMetadata(GitCommit? other) =>
        other is not null
        && ProjectId == other.ProjectId
        && Hash == other.Hash
        && AuthorName == other.AuthorName
        && AuthorEmail == other.AuthorEmail
        && AuthoredAt.EqualsExact(other.AuthoredAt)
        && Subject == other.Subject;
}
