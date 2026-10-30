using DbUp;
using MySqlConnector;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence;

public sealed class MySqlDatabaseInitializer : IDatabaseInitializer
{
    public async Task TestConnectionAsync(
        DatabaseConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new MySqlConnection(MySqlConnectionStrings.Build(settings));
            await connection.OpenAsync(cancellationToken);
        }
        catch (MySqlException exception)
        {
            throw MySqlErrors.Wrap(exception);
        }
    }

    public async Task InitializeAsync(
        DatabaseConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        await TestConnectionAsync(settings, cancellationToken);
        var connectionString = MySqlConnectionStrings.Build(settings);
        var result = await Task.Run(
            () => DeployChanges.To
                .MySqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(typeof(MySqlDatabaseInitializer).Assembly)
                .Build()
                .PerformUpgrade(),
            cancellationToken);

        if (!result.Successful)
        {
            throw new DatabaseAccessException("数据库迁移失败，请检查数据库权限和迁移状态。", result.Error);
        }
    }
}
