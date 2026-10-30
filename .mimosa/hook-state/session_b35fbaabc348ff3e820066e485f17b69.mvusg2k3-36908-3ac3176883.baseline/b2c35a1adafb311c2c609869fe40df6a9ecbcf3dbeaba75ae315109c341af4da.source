using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Persistence;

namespace WeeklyReport.IntegrationTests;

[Collection("MySqlIntegration")]
[Trait("Category", "MySql")]
public class MySqlGitProjectRepositoryTests
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
}
