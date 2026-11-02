namespace WeeklyReport.Application;

internal static class GitSourceValidation
{
    public static (string Path, string Author) Validate(
        Guid projectId, string repositoryPath, GitAuthorField authorField, string author)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("项目标识不能为空。", nameof(projectId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        if (!Path.IsPathFullyQualified(repositoryPath)
            || (OperatingSystem.IsWindows() && repositoryPath.StartsWith(@"\\", StringComparison.Ordinal)))
        {
            throw new ArgumentException("请选择本机的 Git 仓库绝对路径。", nameof(repositoryPath));
        }

        if (!Enum.IsDefined(authorField))
        {
            throw new ArgumentOutOfRangeException(nameof(authorField));
        }

        return (Path.GetFullPath(repositoryPath), author.Trim());
    }
}
