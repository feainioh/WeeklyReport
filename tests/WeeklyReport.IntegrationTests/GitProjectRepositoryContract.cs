// [新增-离线存储] Phase 1 切片 B：IGitProjectRepository 共享契约——绑定/解绑 CRUD、作者筛选字段合法性。
// 基类方法不挂 [Fact]，由子类按各自跳过策略包装（与工作记录/备份契约同模式）。
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.IntegrationTests;

public abstract class GitProjectRepositoryContract
{
    protected abstract Task InitializeAsync();

    protected abstract Task<IGitProjectRepository> CreateRepositoryAsync();

    protected virtual async Task Crud_SupportsBoundAndUnboundProjectsAsync()
    {
        await InitializeAsync();
        var repository = await CreateRepositoryAsync();
        var id = Guid.NewGuid();
        var project = new GitProject(id, "契约项目", Path.GetTempPath(), GitAuthorField.Email, "test@example.invalid");
        var inserted = false;

        try
        {
            await repository.AddAsync(project);
            inserted = true;
            var loaded = await repository.FindAsync(id);
            Assert.NotNull(loaded);
            Assert.True(loaded.IsBound);
            Assert.Equal(project.Name, loaded.Name);
            Assert.Equal(project.RepositoryPath, loaded.RepositoryPath);
            Assert.Equal(project.Author, loaded.Author);

            var unbound = new GitProject(id, "恢复的项目", null, null, null);
            Assert.True(await repository.UpdateAsync(unbound));
            Assert.False((await repository.FindAsync(id))!.IsBound);
            Assert.Contains(await repository.ListAsync(), item => item.Id == id);
            Assert.True(await repository.DeleteAsync(id));
            inserted = false;
            Assert.Null(await repository.FindAsync(id));
        }
        finally
        {
            if (inserted)
            {
                await repository.DeleteAsync(id);
            }
        }
    }
}
