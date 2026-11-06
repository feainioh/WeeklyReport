using System.Data;
using MySqlConnector;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Persistence;

/// <summary>MySQL 本地镜像源数据快照，所有字段来自同一数据库读视图。</summary>
public sealed record MySqlLocalMirrorSnapshot(
    MySqlSyncIdentity Identity,
    IReadOnlyList<GitProject> Projects,
    IReadOnlyList<GitWorkItemSnapshot> WorkItems,
    IReadOnlyList<WorkEntry> WorkEntries,
    IReadOnlyList<WeeklyReportSnapshot> Reports);

/// <summary>在单个 MySQL 一致性事务中读取身份及本地镜像所需的全部业务数据。</summary>
public sealed class MySqlLocalMirrorSnapshotReader
{
    /// <summary>
    /// 读取当前配置数据库的同步身份和镜像数据；任何读取失败都不会返回可用于标记基线的部分结果。
    /// </summary>
    /// <param name="settings">已验证的 MySQL 连接配置。</param>
    /// <param name="cancellationToken">取消连接或快照读取。</param>
    /// <returns>来自单一 Repeatable Read 事务快照的身份及业务数据。</returns>
    public async Task<MySqlLocalMirrorSnapshot> ReadAsync(
        DatabaseConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            await using var connection = new MySqlConnection(MySqlConnectionStrings.Build(settings));
            await connection.OpenAsync(cancellationToken);

            // [新增-离线存储] 先确保 sync_identity 存在（写事务，INSERT IGNORE 幂等），
            // 再以只读事务读取一致性快照——避免只读事务中的 INSERT 报错
            var identity = await EnsureIdentityAsync(connection, cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, isReadOnly: true, cancellationToken: cancellationToken);

            var projects = await ReadProjectsAsync(connection, transaction, cancellationToken);
            var workItems = await ReadWorkItemsAsync(connection, transaction, cancellationToken);
            var workEntries = await ReadWorkEntriesAsync(connection, transaction, cancellationToken);
            var reports = await ReadReportsAsync(connection, transaction, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new MySqlLocalMirrorSnapshot(identity, projects, workItems, workEntries, reports);
        }
        catch (MySqlException exception)
        {
            throw MySqlErrors.Wrap(exception);
        }
    }


    // [重构-统一SqlSugar] MySqlConnector 将 CHAR(36) 的 GUID 格式自动识别为 Guid 对象，
    // GetString() 会抛 InvalidCastException；统一用 Convert.ToString 安全读取
    private static string SafeGetString(MySqlDataReader reader, int ordinal) =>
        Convert.ToString(reader[ordinal], System.Globalization.CultureInfo.InvariantCulture)
        ?? string.Empty;

    // [新增-离线存储] 确保 sync_identity 存在：先尝试读取，不存在则 INSERT IGNORE 初始化（独立写事务）
    private static async Task<MySqlSyncIdentity> EnsureIdentityAsync(
        MySqlConnection connection, CancellationToken cancellationToken)
    {
        // 尝试读取现有身份
        using (var readCommand = connection.CreateCommand())
        {
            readCommand.CommandText = "SELECT database_id, protocol_version FROM sync_identity WHERE id = 1";
            await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var databaseIdText = Convert.ToString(reader[0], System.Globalization.CultureInfo.InvariantCulture);
                if (Guid.TryParse(databaseIdText, out var existingId) && existingId != Guid.Empty)
                {
                    return new MySqlSyncIdentity(existingId, reader.GetInt32(1));
                }
            }
        }

        // 不存在——初始化（独立写事务，INSERT IGNORE 幂等）
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var databaseId = Guid.NewGuid();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT IGNORE INTO sync_identity (id, database_id, protocol_version, created_at_utc) " +
                "VALUES (1, @databaseId, @protocolVersion, @createdAt)";
            command.Parameters.AddWithValue("@databaseId", databaseId.ToString("D"));
            command.Parameters.AddWithValue("@protocolVersion", MySqlSyncIdentityStore.CurrentProtocolVersion);
            command.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
            var inserted = await command.ExecuteNonQueryAsync(cancellationToken);

            if (inserted == 0)
            {
                // 并发初始化：另一端已插入，重新读取
                using var readCommand = connection.CreateCommand();
                readCommand.Transaction = transaction;
                readCommand.CommandText = "SELECT database_id, protocol_version FROM sync_identity WHERE id = 1";
                await using var readReader = await readCommand.ExecuteReaderAsync(cancellationToken);
                if (await readReader.ReadAsync(cancellationToken))
                {
                    var existingText = Convert.ToString(readReader[0], System.Globalization.CultureInfo.InvariantCulture);
                    if (Guid.TryParse(existingText, out var concurrentId))
                    {
                        await transaction.CommitAsync(cancellationToken);
                        return new MySqlSyncIdentity(concurrentId, readReader.GetInt32(1));
                    }
                }
                await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("MySQL 同步身份初始化失败。");
            }

            await transaction.CommitAsync(cancellationToken);
            return new MySqlSyncIdentity(databaseId, MySqlSyncIdentityStore.CurrentProtocolVersion);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<IReadOnlyList<GitProject>> ReadProjectsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var rows = await ReadRowsAsync(connection, transaction,
            "SELECT id, name, repository_path, author_field, author_value " +
            "FROM git_projects ORDER BY id",
            reader => new GitProjectRow
            {
                Id = SafeGetString(reader, 0),
                Name = SafeGetString(reader, 1),
                RepositoryPath = ReadNullableString(reader, 2),
                AuthorField = ReadNullableString(reader, 3),
                Author = ReadNullableString(reader, 4)
            },
            cancellationToken);
        return rows.Select(SqlSugarGitProjectRepository.ToProject).ToArray();
    }

    private static async Task<IReadOnlyList<GitWorkItemSnapshot>> ReadWorkItemsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var items = await ReadRowsAsync(connection, transaction,
            "SELECT id, project_id, work_date, description, estimated_minutes, confirmed_minutes, " +
            "is_deleted, revision FROM git_work_items ORDER BY id",
            reader => new GitWorkItemRow
            {
                Id = SafeGetString(reader, 0),
                ProjectId = SafeGetString(reader, 1),
                WorkDate = reader.GetDateTime(2),
                Description = SafeGetString(reader, 3),
                EstimatedMinutes = ReadNullableInt32(reader, 4),
                ConfirmedMinutes = ReadNullableInt32(reader, 5),
                IsDeleted = reader.GetBoolean(6),
                Revision = reader.GetInt64(7)
            },
            cancellationToken);
        var links = await ReadRowsAsync(connection, transaction,
            "SELECT project_id, commit_hash, work_item_id, author_name, author_email, authored_at, subject " +
            "FROM git_commit_links ORDER BY project_id, commit_hash",
            reader => new GitCommitRow
            {
                ProjectId = SafeGetString(reader, 0),
                CommitHash = SafeGetString(reader, 1),
                WorkItemId = SafeGetString(reader, 2),
                AuthorName = SafeGetString(reader, 3),
                AuthorEmail = SafeGetString(reader, 4),
                AuthoredAt = SafeGetString(reader, 5),
                Subject = SafeGetString(reader, 6)
            },
            cancellationToken);
        return SqlSugarGitSyncStore.MapSnapshots(items, links);
    }

    private static async Task<IReadOnlyList<WorkEntry>> ReadWorkEntriesAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var rows = await ReadRowsAsync(connection, transaction,
            "SELECT id, work_date, content, created_at_utc, updated_at_utc, confirmed_minutes " +
            "FROM work_entries ORDER BY work_date, id",
            reader => new WorkEntryRow
            {
                Id = SafeGetString(reader, 0),
                WorkDate = reader.GetDateTime(1),
                Content = SafeGetString(reader, 2),
                CreatedAtUtc = reader.GetDateTime(3),
                UpdatedAtUtc = reader.GetDateTime(4),
                ConfirmedMinutes = ReadNullableInt32(reader, 5)
            },
            cancellationToken);
        return rows.Select(SqlSugarWorkEntryRepository.ToEntry).ToArray();
    }

    private static async Task<IReadOnlyList<WeeklyReportSnapshot>> ReadReportsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var rows = await ReadRowsAsync(connection, transaction,
            "SELECT id, week_start, week_end, work_summary, next_week_plan, risks, confirmed_minutes, " +
            "estimated_minutes, unknown_hours_count, created_at_utc, updated_at_utc " +
            "FROM weekly_reports ORDER BY week_start, id",
            reader => new WeeklyReportRow
            {
                Id = SafeGetString(reader, 0),
                WeekStart = reader.GetDateTime(1),
                WeekEnd = reader.GetDateTime(2),
                WorkSummary = SafeGetString(reader, 3),
                NextWeekPlan = SafeGetString(reader, 4),
                Risks = SafeGetString(reader, 5),
                ConfirmedMinutes = reader.GetInt32(6),
                EstimatedMinutes = reader.GetInt32(7),
                UnknownHoursCount = reader.GetInt32(8),
                CreatedAtUtc = reader.GetDateTime(9),
                UpdatedAtUtc = reader.GetDateTime(10)
            },
            cancellationToken);
        return rows.Select(SqlSugarWeeklyReportRepository.ToReport).ToArray();
    }

    private static async Task<List<T>> ReadRowsAsync<T>(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string query,
        Func<MySqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        using var command = CreateCommand(connection, transaction, query);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private static MySqlCommand CreateCommand(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string query)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query;
        return command;
    }

    private static string? ReadNullableString(MySqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? ReadNullableInt32(MySqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
}
