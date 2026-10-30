using MySqlConnector;
using WeeklyReport.Application;

namespace WeeklyReport.IntegrationTests;

[CollectionDefinition("MySqlIntegration", DisableParallelization = true)]
public sealed class MySqlIntegrationCollection;

internal static class MySqlIntegrationSupport
{
    public static DatabaseConnectionSettings LoadSettings()
    {
        var connectionString = Environment.GetEnvironmentVariable("WEEKLYREPORT_TEST_CONNECTION")
            ?? throw new InvalidOperationException("缺少隔离测试库连接配置。");
        var builder = new MySqlConnectionStringBuilder(connectionString);
        if (!builder.Database.StartsWith("weeklyreport_test_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("集成测试只能使用 weeklyreport_test_ 前缀的独立数据库。");
        }

        return new DatabaseConnectionSettings(
            builder.Server, (int)builder.Port, builder.Database, builder.UserID, builder.Password);
    }

    public static MySqlConnection CreateConnection(DatabaseConnectionSettings settings) =>
        new(new MySqlConnectionStringBuilder
        {
            Server = settings.Host,
            Port = (uint)settings.Port,
            Database = settings.Database,
            UserID = settings.UserName,
            Password = settings.Password
        }.ConnectionString);

    public sealed class FixedConfigurationStore(DatabaseConnectionSettings settings) : IDatabaseConfigurationStore
    {
        public Task<DatabaseConnectionSettings?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<DatabaseConnectionSettings?>(settings);

        public Task SaveAsync(DatabaseConnectionSettings value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
