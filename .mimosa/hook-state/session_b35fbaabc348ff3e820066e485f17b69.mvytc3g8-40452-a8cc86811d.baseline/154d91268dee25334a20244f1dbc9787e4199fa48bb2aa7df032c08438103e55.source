using SqlSugar;
using WeeklyReport.Infrastructure.Persistence;

namespace WeeklyReport.IntegrationTests;

public class SqlSugarMappingTests
{
    [Fact]
    public void Query_UsesMappedTableAndParameterizesContent()
    {
        using var database = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Server=127.0.0.1;Database=weeklyreport_test_example;User ID=test;Password=not-a-real-secret",
            DbType = DbType.MySql,
            IsAutoCloseConnection = true
        });
        const string content = "测试 ' OR 1=1 --";

        var sql = database.Queryable<WorkEntryRow>().Where(row => row.Content == content).ToSql();

        Assert.Contains("work_entries", sql.Key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(content, sql.Key, StringComparison.Ordinal);
        Assert.Contains(sql.Value, parameter => Equals(parameter.Value, content));
    }

    [Fact]
    public void MigrationScript_IsEmbeddedInInfrastructureAssembly()
    {
        var resources = typeof(MySqlDatabaseInitializer).Assembly.GetManifestResourceNames();

        Assert.Contains(resources, name => name.EndsWith("001_CreateWorkEntries.sql", StringComparison.Ordinal));
        Assert.Contains(resources, name => name.EndsWith("002_AddWorkEntryHours.sql", StringComparison.Ordinal));
        Assert.Contains(resources, name => name.EndsWith("003_CreateWeeklyReports.sql", StringComparison.Ordinal));
        Assert.Contains(resources, name => name.EndsWith("004_CreateGitTables.sql", StringComparison.Ordinal));
    }
}
