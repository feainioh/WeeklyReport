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
                // [旧代码-离线存储] 原无过滤的嵌入脚本扫描——SQLite 迁移脚本嵌入同一程序集后会被误执行到 MySQL
                // .WithScriptsEmbeddedInAssembly(typeof(MySqlDatabaseInitializer).Assembly)
                // [新增-离线存储] Phase 1 切片 A：限定只执行 MySQL 迁移目录（Persistence.Migrations）下的脚本
                .WithScriptsEmbeddedInAssembly(
                    typeof(MySqlDatabaseInitializer).Assembly,
                    name => name.EndsWith(".sql", StringComparison.Ordinal)
                        && name.Contains(".Persistence.Migrations.", StringComparison.Ordinal))
                .Build()
                .PerformUpgrade(),
            cancellationToken);

        if (!result.Successful)
        {
            throw new DatabaseAccessException("数据库迁移失败，请检查数据库权限和迁移状态。", result.Error);
        }
    }
}
