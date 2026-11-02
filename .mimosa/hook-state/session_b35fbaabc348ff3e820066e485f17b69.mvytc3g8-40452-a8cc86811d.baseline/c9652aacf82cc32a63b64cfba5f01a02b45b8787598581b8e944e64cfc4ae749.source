namespace WeeklyReport.Application;

public sealed class GitProject
{
    public GitProject(
        Guid id,
        string name,
        string? repositoryPath,
        GitAuthorField? authorField,
        string? author)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("项目标识不能为空。", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        if (name.Length > 200)
        {
            throw new ArgumentException("项目名称不能超过 200 个字符。", nameof(name));
        }

        var unbound = repositoryPath is null && authorField is null && author is null;
        if (!unbound)
        {
            if (repositoryPath is null || authorField is null || author is null)
            {
                throw new ArgumentException("仓库路径、作者类型和作者必须同时配置。");
            }

            var source = GitSourceValidation.Validate(id, repositoryPath, authorField.Value, author);
            RepositoryPath = source.Path;
            AuthorField = authorField;
            Author = source.Author;
        }

        Id = id;
        Name = name;
    }

    public Guid Id { get; }
    public string Name { get; }
    public string? RepositoryPath { get; }
    public GitAuthorField? AuthorField { get; }
    public string? Author { get; }
    public bool IsBound => RepositoryPath is not null;
}
