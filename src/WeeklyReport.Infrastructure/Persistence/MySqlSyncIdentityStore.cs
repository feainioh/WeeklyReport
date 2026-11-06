// [新增-离线存储] Phase 2：读取 MySQL 的稳定数据库身份和同步协议版本。
using MySqlConnector;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence;

/// <summary>MySQL 数据库的稳定身份与同步协议版本。</summary>
public sealed record MySqlSyncIdentity(Guid DatabaseId, int ProtocolVersion);

/// <summary>读取 MySQL 同步协议元数据，不会创建或修改远程身份。</summary>
public sealed class MySqlSyncIdentityStore
{
    /// <summary>当前同步协议版本；数据库与本地元数据必须完全匹配。</summary>
    public const int CurrentProtocolVersion = 1;

    /// <summary>读取预先初始化的数据库身份；缺失或格式错误时拒绝将其当作有效同步目标。</summary>
    /// <param name="settings">当前配置的 MySQL 连接。</param>
    /// <param name="cancellationToken">取消连接和读取。</param>
    /// <returns>稳定数据库 ID 与协议版本。</returns>
    public async Task<MySqlSyncIdentity> GetAsync(
        DatabaseConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            await using var connection = new MySqlConnection(MySqlConnectionStrings.Build(settings));
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT database_id, protocol_version FROM sync_identity WHERE id = 1";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await ReadAsync(reader, cancellationToken);
        }
        catch (MySqlException exception)
        {
            throw MySqlErrors.Wrap(exception);
        }
    }

    internal static async Task<MySqlSyncIdentity> ReadAsync(
        MySqlDataReader reader,
        CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "MySQL 同步身份尚未初始化，请在设置中执行“初始化并保存”后重试。");
        }

        // [重构-统一SqlSugar] MySqlConnector 将 CHAR(36) 的 GUID 格式自动识别为 Guid 对象，
        // GetString() 会抛 InvalidCastException；用 Convert.ToString 统一处理两种类型
        var databaseIdText = Convert.ToString(reader[0], System.Globalization.CultureInfo.InvariantCulture);
        if (!Guid.TryParse(databaseIdText, out var databaseId))
        {
            throw new InvalidOperationException("MySQL 同步身份格式无效，已拒绝同步。");
        }
        if (databaseId == Guid.Empty)
        {
            throw new InvalidOperationException("MySQL 同步身份不能是空 GUID，已拒绝同步。");
        }

        var protocolVersion = reader.GetInt32(1);
        if (protocolVersion != CurrentProtocolVersion)
        {
            throw new InvalidOperationException(
                $"MySQL 同步协议版本 {protocolVersion} 与当前版本 {CurrentProtocolVersion} 不兼容。");
        }

        return new MySqlSyncIdentity(databaseId, protocolVersion);
    }
}
