using WeeklyReport.Application;

namespace WeeklyReport.UnitTests;

public class DatabaseConnectionSettingsTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Constructor_AcceptsLocalHosts(string host)
    {
        var settings = new DatabaseConnectionSettings(host, 3306, "weekly_report", "test-user", "test-secret");

        Assert.Equal(host, settings.Host);
        Assert.DoesNotContain("test-secret", settings.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("example.invalid")]
    [InlineData("192.0.2.1")]
    public void Constructor_RejectsRemoteHosts(string host)
    {
        Assert.Throws<ArgumentException>(
            () => new DatabaseConnectionSettings(host, 3306, "weekly_report", "test-user", "test-secret"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Constructor_RejectsInvalidPorts(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DatabaseConnectionSettings("localhost", port, "weekly_report", "test-user", "test-secret"));
    }

    [Fact]
    public void Constructor_RejectsInvalidDatabaseName()
    {
        Assert.Throws<ArgumentException>(
            () => new DatabaseConnectionSettings("localhost", 3306, "bad;name", "test-user", "test-secret"));
    }
}
