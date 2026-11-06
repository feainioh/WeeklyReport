using SqlSugar;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence;

public sealed class SqlSugarGitProjectRepository(IDatabaseConfigurationStore configurationStore) : IGitProjectRepository
{
    private readonly SqlSugarExecutor _executor = new(configurationStore);

    public async Task<IReadOnlyList<GitProject>> ListAsync() =>
        await _executor.ExecuteAsync<IReadOnlyList<GitProject>>(async database =>
        {
            var rows = await database.Queryable<GitProjectRow>().OrderBy(row => row.Name, OrderByType.Asc).ToListAsync();
            return rows.Select(ToProject).ToArray();
        });

    public async Task<GitProject?> FindAsync(Guid id)
    {
        var key = id.ToString("D");
        return await _executor.ExecuteAsync(async database =>
        {
            var row = await database.Queryable<GitProjectRow>().FirstAsync(item => item.Id == key);
            return row is null ? null : ToProject(row);
        });
    }

    public async Task AddAsync(GitProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateText(project);
        await _executor.ExecuteAsync(async database => await database.Insertable(ToRow(project)).ExecuteCommandAsync());
    }

    public async Task<bool> UpdateAsync(GitProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        ValidateText(project);
        return await _executor.ExecuteAsync(async database =>
            await database.Updateable(ToRow(project))
                .UpdateColumns(row => new { row.Name, row.RepositoryPath, row.AuthorField, row.Author })
                .ExecuteCommandAsync() > 0);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var key = id.ToString("D");
        return await _executor.ExecuteAsync(async database =>
            await database.Deleteable<GitProjectRow>().Where(row => row.Id == key).ExecuteCommandAsync() > 0);
    }

    private static GitProjectRow ToRow(GitProject project) => new()
    {
        Id = project.Id.ToString("D"),
        Name = project.Name,
        RepositoryPath = project.RepositoryPath,
        AuthorField = project.AuthorField?.ToString(),
        Author = project.Author
    };

    internal static GitProject ToProject(GitProjectRow row)
    {
        GitAuthorField? field = null;
        if (row.AuthorField is not null)
        {
            if (!Enum.TryParse<GitAuthorField>(row.AuthorField, out var parsed)
                || !Enum.IsDefined(parsed)
                || parsed.ToString() != row.AuthorField)
            {
                throw new InvalidDataException("项目作者筛选类型无效，请检查数据库。");
            }

            field = parsed;
        }

        return new GitProject(Guid.Parse(row.Id), row.Name, row.RepositoryPath, field, row.Author);
    }

    private static void ValidateText(GitProject project)
    {
        DbTextLimits.Validate(project.Name, "项目名称");
        if (project.RepositoryPath is not null)
        {
            DbTextLimits.Validate(project.RepositoryPath, "仓库路径");
        }

        if (project.Author is not null)
        {
            DbTextLimits.Validate(project.Author, "作者筛选");
        }
    }
}
