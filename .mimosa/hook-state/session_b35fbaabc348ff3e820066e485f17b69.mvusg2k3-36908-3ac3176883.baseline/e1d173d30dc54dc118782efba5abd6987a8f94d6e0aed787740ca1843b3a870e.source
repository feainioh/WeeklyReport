using WeeklyReport.Application;

namespace WeeklyReport.UnitTests;

public class GitProjectTests
{
    [Fact]
    public void UnboundProject_CanBeRestoredWithoutLocalPathOrAuthor()
    {
        var project = new GitProject(Guid.NewGuid(), "项目", null, null, null);

        Assert.False(project.IsBound);
        Assert.Null(project.RepositoryPath);
        Assert.Null(project.Author);
    }

    [Fact]
    public void BoundProject_NormalizesNameAndAuthor()
    {
        var path = Path.Combine(Path.GetTempPath(), "weeklyreport-project");
        var project = new GitProject(Guid.NewGuid(), "  项目  ", path, GitAuthorField.Email, "  test@example.invalid  ");

        Assert.True(project.IsBound);
        Assert.Equal("项目", project.Name);
        Assert.Equal("test@example.invalid", project.Author);
        Assert.Equal(Path.GetFullPath(path), project.RepositoryPath);
    }

    [Fact]
    public void PartialBinding_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new GitProject(Guid.NewGuid(), "项目", Path.GetTempPath(), null, "name"));
    }

    [Fact]
    public void LongName_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new GitProject(Guid.NewGuid(), new string('a', 201), null, null, null));
    }
}
