using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Persistence;

namespace WeeklyReport.IntegrationTests;

[Collection("MySqlIntegration")]
[Trait("Category", "MySql")]
// [新增-离线存储] Phase 1 切片 B：接入 Git 项目仓储共享契约
public class MySqlGitProjectRepositoryTests : GitProjectRepositoryContract
{
    [MySqlIntegrationFact]
    public async Task Crud_SupportsBoundAndUnboundProjects()
    {
        var settings = MySqlIntegrationSupport.LoadSettings();
        await new MySqlDatabaseInitializer().InitializeAsync(settings);
        var repository = new SqlSugarGitProjectRepository(new MySqlIntegrationSupport.FixedConfigurationStore(settings));
        var id = Guid.NewGuid();
        var project = new GitProject(id, "测试项目", Path.GetTempPath(), GitAuthorField.Email, "test@example.invalid");
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

    // [新增-离线存储] Phase 1 切片 B：契约钩子与包装（原有测试保持原样）
    protected override Task InitializeAsync() =>
        new MySqlDatabaseInitializer().InitializeAsync(MySqlIntegrationSupport.LoadSettings());

    protected override Task<IGitProjectRepository> CreateRepositoryAsync() => Task.FromResult<IGitProjectRepository>(
        new SqlSugarGitProjectRepository(new MySqlIntegrationSupport.FixedConfigurationStore(
            MySqlIntegrationSupport.LoadSettings())));

    [MySqlIntegrationFact]
    public async Task Contract_Crud_SupportsBoundAndUnboundProjects() =>
        await Crud_SupportsBoundAndUnboundProjectsAsync();
}
