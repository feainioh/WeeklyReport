// [新增-离线存储] Phase 2 G1：同步应用器——从 SQLite 取待同步操作组，在 MySQL 侧以幂等回执协议应用。
// 协议（ADR-03 §4.3）：同一 InnoDB 事务内完成身份/协议校验 → 回执去重 → 业务写入 → 回执插入 → 提交。
// v2 操作组保存不可变前/后快照，安全新增直接使用入队快照，不从后续 SQLite 行重建内容。
// 旧版无快照队列及缺少可验证远端基线的更新/删除均保留为冲突，不自动覆盖 MySQL。
//
// [重构-统一SqlSugar] 原 MySQL 侧使用原生 MySqlConnection/MySqlCommand 手动参数绑定与类型转换，
// 导致 SQLite→MySQL 时间戳/整数类型不匹配。现统一改用 SqlSugarClient(DbType.MySql)：
//   - FOR UPDATE 查询：Ado.SqlQueryAsync<T>(sql, anonymousParams)
//   - INSERT：Insertable(MySQL 行类型) —— SqlSugar 自动处理 DateTime ↔ DATETIME 类型映射
//   - 回执/元数据：Ado.ExecuteCommandAsync(sql, anonymousParams)
//   - 事务：Ado.BeginTranAsync(Serializable) → CommitTranAsync / RollbackTranAsync
//   - 异常：SqlSugarException.InnerException is MySqlException → 按原逻辑分类处理
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using SqlSugar;
using WeeklyReport.Application;
using WeeklyReport.Domain;
using WeeklyReport.Infrastructure.Persistence;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

public sealed record SyncOutcome(int Applied, int Conflicted, int Failed, string? Error);

/// <summary>同步协调器接口（Application 层，UI/设置页消费）。</summary>
public interface ILocalSyncCoordinator
{
    Task<SyncOutcome> SyncNowAsync(CancellationToken cancellationToken = default);
}

public sealed class SqliteSyncApplier(string databasePath, IDatabaseConfigurationStore configurationStore)
{
    private const int SupportedProtocolVersion = MySqlSyncIdentityStore.CurrentProtocolVersion;
    private static readonly HashSet<string> SupportedTables =
        new(StringComparer.Ordinal)
        {
            "work_entries",
            "weekly_reports",
            "git_projects",
            "git_work_items"
        };

    private readonly SqliteLocalStore _localStore = new(databasePath);

    // [重构-统一SqlSugar] 创建 MySQL 侧 SqlSugar 客户端（与 SqlSugarExecutor 同构，但手动管理事务）
    private static SqlSugarClient CreateMySqlClient(DatabaseConnectionSettings settings)
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = new MySqlConnectionStringBuilder
            {
                Server = settings.Host,
                Port = (uint)settings.Port,
                Database = settings.Database,
                UserID = settings.UserName,
                Password = settings.Password,
                AllowUserVariables = true
            }.ConnectionString,
            DbType = DbType.MySql,
            IsAutoCloseConnection = false // 事务期间保持连接
        });
    }

    public async Task<SyncOutcome> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        var pending = await LoadPendingOperationsAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return new SyncOutcome(0, 0, 0, null);
        }

        var conflicted = 0;
        var validOperations = new List<(PendingOperation Operation, LocalOperationChange[] Changes)>();
        foreach (var operation in pending)
        {
            try
            {
                validOperations.Add((operation, ParseChanges(
                    operation.ChangesJson, operation.PayloadVersion, operation.ContentHash)));
            }
            catch (SyncOperationException exception)
            {
                await MarkConflictAsync(operation, exception.Message, cancellationToken);
                conflicted++;
            }
            catch (JsonException)
            {
                await MarkConflictAsync(operation, "操作组变更数据格式无效，已暂停该组。", cancellationToken);
                conflicted++;
            }
        }

        if (validOperations.Count == 0)
        {
            return new SyncOutcome(0, conflicted, 0, null);
        }

        var settings = await configurationStore.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException("尚未配置 MySQL，无法同步。");
        MySqlSyncIdentity localIdentity;
        try
        {
            localIdentity = await LoadLocalIdentityAsync(cancellationToken);
        }
        catch (SyncIdentityException exception)
        {
            await _localStore.SuspendSyncAsync(exception.Message, cancellationToken);
            return new SyncOutcome(0, conflicted, validOperations.Count, exception.Message);
        }

        var applied = 0;
        var failed = 0;
        string? lastError = null;

        for (var index = 0; index < validOperations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (operation, changes) = validOperations[index];
            try
            {
                var result = await ApplyOperationAsync(settings, operation, changes, localIdentity, cancellationToken);
                await _localStore.ResumeSyncAsync(cancellationToken);
                if (result == ApplyResult.Applied || result == ApplyResult.AlreadyApplied)
                {
                    await MarkSyncedAsync(operation.OpGroupId, cancellationToken);
                    applied++;
                }
                else if (result == ApplyResult.Conflict)
                {
                    await MarkConflictAsync(operation, "MySQL 侧数据已变化", cancellationToken);
                    conflicted++;
                }
            }
            catch (SyncIdentityException exception)
            {
                lastError = exception.Message;
                await _localStore.SuspendSyncAsync(lastError, cancellationToken);
                failed += validOperations.Count - index;
                break;
            }
            catch (MySqlException exception)
            {
                if (exception.Number == 1062)
                {
                    await MarkConflictAsync(operation, "MySQL 中已存在相同主键或自然键，未覆盖现有数据。", cancellationToken);
                    conflicted++;
                    continue;
                }

                lastError = exception.Number == 1146
                    ? "MySQL 同步协议表不存在，请在设置中执行\"初始化并保存\"后重试。"
                    : $"MySQL 连接/权限错误：{exception.Message}";
                if (exception.Number == 1146)
                {
                    await _localStore.SuspendSyncAsync(lastError, cancellationToken);
                    failed += validOperations.Count - index;
                }
                else
                {
                    failed++;
                }

                break;
            }
            catch (SqlSugarException exception)
            {
                // [重构-统一SqlSugar] SqlSugar 包装后的 MySQL 异常解包
                if (exception.InnerException is MySqlException mysqlException)
                {
                    if (mysqlException.Number == 1062)
                    {
                        await MarkConflictAsync(operation, "MySQL 中已存在相同主键或自然键，未覆盖现有数据。", cancellationToken);
                        conflicted++;
                        continue;
                    }
                    lastError = $"MySQL 操作失败：{mysqlException.Message}";
                }
                else
                {
                    lastError = $"数据库操作失败：{exception.Message}";
                }
                failed++;
                break;
            }
            catch (InvalidOperationException exception)
            {
                lastError = exception.Message;
                await MarkConflictAsync(operation, lastError, cancellationToken);
                conflicted++;
            }
        }

        if (applied > 0)
        {
            await UpdateLastSyncTimeAsync(cancellationToken);
        }

        return new SyncOutcome(applied, conflicted, failed, lastError);
    }

    private enum ApplyResult { Applied, AlreadyApplied, Conflict }

    private sealed class SyncIdentityException(string message) : Exception(message);

    private sealed class SyncOperationException(string message) : Exception(message);

    // —— 快照解析与校验（纯逻辑，无数据库操作）——

    private static LocalOperationChange[] ParseChanges(
        string changesJson,
        int payloadVersion,
        string contentHash)
    {
        if (payloadVersion != SqliteOperationPayload.CurrentVersion)
        {
            throw new SyncOperationException("旧版操作组缺少不可变快照，已保留为冲突，未从当前本地数据补造内容。");
        }

        if (!string.Equals(
                SqliteOperationPayload.ComputeHash(changesJson),
                contentHash,
                StringComparison.Ordinal))
        {
            throw new SyncOperationException("操作组内容哈希不匹配，已保留为冲突。");
        }

        var changes = JsonSerializer.Deserialize<LocalOperationChange[]>(changesJson)
            ?? throw new SyncOperationException("操作组没有有效的变更集合，已保留为冲突。");
        if (changes.Length == 0)
        {
            throw new SyncOperationException("空操作组不能安全同步，已保留为冲突。");
        }

        foreach (var change in changes)
        {
            if (change is null
                || !SupportedTables.Contains(change.Table)
                || !Guid.TryParse(change.RowId, out _)
                || string.IsNullOrWhiteSpace(change.NaturalKey)
                || !Enum.IsDefined(change.Change))
            {
                throw new SyncOperationException("操作组包含未知或无效变更，已保留为冲突。");
            }

            if (change.Change == LocalObjectChange.Add)
            {
                if (!string.IsNullOrWhiteSpace(change.BeforeJson)
                    || string.IsNullOrWhiteSpace(change.AfterJson))
                {
                    throw new SyncOperationException("新增操作缺少有效的不可变快照，已保留为冲突。");
                }

                ValidateAfterSnapshot(change);
            }
            else
            {
                throw new SyncOperationException(
                    "操作组缺少可验证的变更前基线，更新或删除已保留为冲突，未自动覆盖 MySQL。");
            }
        }

        return changes;
    }

    private static void ValidateAfterSnapshot(LocalOperationChange change)
    {
        try
        {
            var rowId = Guid.Parse(change.RowId);
            var isValid = change.Table switch
            {
                "work_entries" => IsMatchingWorkEntry(change, rowId),
                "weekly_reports" => IsMatchingWeeklyReport(change, rowId),
                "git_projects" => IsMatchingGitProject(change, rowId),
                "git_work_items" => IsMatchingGitWorkItem(change, rowId),
                _ => false
            };
            if (!isValid)
            {
                throw new SyncOperationException("操作组快照与对象标识或自然键不匹配，已保留为冲突。");
            }
        }
        catch (SyncOperationException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw InvalidSnapshotException();
        }
        catch (ArgumentException)
        {
            throw InvalidSnapshotException();
        }
        catch (FormatException)
        {
            throw InvalidSnapshotException();
        }
        catch (InvalidOperationException)
        {
            throw InvalidSnapshotException();
        }
        catch (NotSupportedException)
        {
            throw InvalidSnapshotException();
        }
    }

    private static SyncOperationException InvalidSnapshotException() =>
        new("操作组快照格式无效，已保留为冲突。");

    private static bool IsMatchingWorkEntry(LocalOperationChange change, Guid rowId)
    {
        var entry = SqliteOperationPayload.Deserialize<WorkEntry>(change.AfterJson!);
        return entry.Id == rowId
            && change.NaturalKey == entry.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static bool IsMatchingWeeklyReport(LocalOperationChange change, Guid rowId)
    {
        var report = SqliteOperationPayload.DeserializeWeeklyReport(change.AfterJson!);
        return report.Id == rowId
            && change.NaturalKey == report.Week.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static bool IsMatchingGitProject(LocalOperationChange change, Guid rowId)
    {
        var project = SqliteOperationPayload.Deserialize<GitProject>(change.AfterJson!);
        return project.Id == rowId && change.NaturalKey == project.Name;
    }

    private static bool IsMatchingGitWorkItem(LocalOperationChange change, Guid rowId)
    {
        var snapshot = SqliteOperationPayload.DeserializeGitWorkItem(change.AfterJson!);
        var item = snapshot.WorkItem;
        return item.Id == rowId
            && snapshot.Project.Id == item.ProjectId
            && change.NaturalKey == $"{item.ProjectId:D}|{item.WorkDate:yyyy-MM-dd}";
    }

    // —— 身份管理 ——

    private async Task<MySqlSyncIdentity> LoadLocalIdentityAsync(CancellationToken cancellationToken)
    {
        await SqliteDatabase.EnsureInitializedAsync(databasePath);
        await using var connection = SqliteDatabase.Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT mirror_identity, protocol_version, storage_status FROM local_meta WHERE id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || reader.GetString(2) is not ("Ready" or "Suspended")
            || !Guid.TryParse(reader.IsDBNull(0) ? null : reader.GetString(0), out var databaseId))
        {
            throw new SyncIdentityException(
                "本地存储缺少已验证的 MySQL 数据库身份，已暂停同步并保留待同步操作。");
        }

        var protocolVersion = reader.GetInt32(1);
        if (protocolVersion != SupportedProtocolVersion)
        {
            throw new SyncIdentityException(
                $"本地同步协议版本 {protocolVersion} 不受支持，当前版本为 {SupportedProtocolVersion}。");
        }

        return new MySqlSyncIdentity(databaseId, protocolVersion);
    }

    // [重构-统一SqlSugar] 身份校验——SqlSugar 不支持值元组映射，用 DTO 类
    private sealed class SyncIdentityRow
    {
        public Guid DatabaseId { get; set; }
        public int ProtocolVersion { get; set; }
    }

    private static async Task ValidateRemoteIdentityAsync(
        SqlSugarClient database,
        MySqlSyncIdentity localIdentity,
        CancellationToken cancellationToken)
    {
        var rows = await database.Ado.SqlQueryAsync<SyncIdentityRow>(
            "SELECT database_id AS DatabaseId, protocol_version AS ProtocolVersion FROM sync_identity WHERE id = 1 FOR UPDATE");
        var row = rows.FirstOrDefault();
        if (row is null || row.DatabaseId == Guid.Empty)
        {
            throw new SyncIdentityException(
                "MySQL 同步身份未初始化或格式无效，请在设置中执行\"初始化并保存\"后重试。");
        }

        if (row.DatabaseId != localIdentity.DatabaseId)
        {
            throw new SyncIdentityException(
                "MySQL 数据库身份与本地镜像不匹配，已暂停同步以避免向错误数据库重放变更。");
        }

        if (row.ProtocolVersion != SupportedProtocolVersion
            || row.ProtocolVersion != localIdentity.ProtocolVersion)
        {
            throw new SyncIdentityException(
                $"MySQL 同步协议版本 {row.ProtocolVersion} 与本地版本不兼容，已暂停同步。");
        }
    }

    // [重构-统一SqlSugar] 核心变更：MySQL 侧操作全部改用 SqlSugarClient 事务
    private static async Task<ApplyResult> ApplyOperationAsync(
        DatabaseConnectionSettings settings,
        PendingOperation operation,
        IReadOnlyList<LocalOperationChange> changes,
        MySqlSyncIdentity localIdentity,
        CancellationToken ct)
    {
        using var database = CreateMySqlClient(settings);
        await database.Ado.BeginTranAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            await ValidateRemoteIdentityAsync(database, localIdentity, ct);

            // 1. 幂等回执检查（FOR UPDATE 行锁防止并发重放）
            var receiptRows = await database.Ado.SqlQueryAsync<string>(
                "SELECT content_hash FROM sync_receipt WHERE op_group_id = @id FOR UPDATE",
                new { id = operation.OpGroupId });
            if (receiptRows.Count > 0)
            {
                if (receiptRows[0] == operation.ContentHash)
                {
                    await database.Ado.CommitTranAsync();
                    return ApplyResult.AlreadyApplied; // 幂等：响应丢失后重试
                }
                throw new InvalidOperationException($"操作组 {operation.OpGroupId} 内容哈希不一致，拒绝重放。");
            }

            // 2. 检查依赖组已完成
            if (operation.DependsOn is not null)
            {
                var depRows = await database.Ado.SqlQueryAsync<long>(
                    "SELECT COUNT(*) FROM sync_receipt WHERE op_group_id = @dep",
                    new { dep = operation.DependsOn });
                if (depRows.Count == 0 || depRows[0] == 0)
                {
                    await database.Ado.RollbackTranAsync();
                    return ApplyResult.Conflict; // 依赖组未同步
                }
            }

            // 3. 应用变更（经 MySQL 行类型 + SqlSugar 自动类型映射，消除手动转换）
            await ApplyChangesAsync(database, changes, ct);

            // 4. 写入回执
            await database.Ado.ExecuteCommandAsync(
                "INSERT INTO sync_receipt (op_group_id, content_hash, applied_at_utc, result) " +
                "VALUES (@id, @hash, @at, 'applied')",
                new
                {
                    id = operation.OpGroupId,
                    hash = operation.ContentHash,
                    at = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)
                });

            await database.Ado.CommitTranAsync();
            return ApplyResult.Applied;
        }
        catch
        {
            await database.Ado.RollbackTranAsync();
            throw;
        }
    }

    // [重构-统一SqlSugar] 变更应用：使用 MySQL 行类型 + Insertable 自动类型映射
    private static async Task ApplyChangesAsync(
        SqlSugarClient database,
        IReadOnlyList<LocalOperationChange> changes,
        CancellationToken ct)
    {
        foreach (var change in changes)
        {
            switch (change.Table)
            {
                case "work_entries":
                    await ApplyWorkEntryAsync(database, change);
                    break;
                case "weekly_reports":
                    await ApplyWeeklyReportAsync(database, change);
                    break;
                case "git_projects":
                    await ApplyGitProjectAsync(database, change);
                    break;
                case "git_work_items":
                    await ApplyGitWorkItemAsync(database, change);
                    break;
                default:
                    throw new InvalidOperationException($"不支持同步表：{change.Table}");
            }
        }
    }

    // [重构-统一SqlSugar] 工作记录：快照 → MySQL 行类型 → Insertable（DateTime 自动转换）
    private static async Task ApplyWorkEntryAsync(SqlSugarClient database, LocalOperationChange change)
    {
        var entry = SqliteOperationPayload.Deserialize<WorkEntry>(change.AfterJson!);
        var existing = await database.Ado.SqlQueryAsync<string>(
            "SELECT id FROM work_entries WHERE id = @id FOR UPDATE",
            new { id = entry.Id.ToString("D") });
        if (existing.Count > 0)
        {
            throw new InvalidOperationException("MySQL 已存在相同主键的工作记录，拒绝覆盖。");
        }

        await database.Insertable(new WorkEntryRow
        {
            Id = entry.Id.ToString("D"),
            WorkDate = entry.WorkDate.ToDateTime(TimeOnly.MinValue), // SqlSugar → MySQL DATE
            Content = entry.Content,
            CreatedAtUtc = entry.CreatedAtUtc.UtcDateTime,            // SqlSugar → MySQL DATETIME
            UpdatedAtUtc = entry.UpdatedAtUtc.UtcDateTime,
            ConfirmedMinutes = entry.ConfirmedMinutes
        }).ExecuteCommandAsync();
    }

    // [重构-统一SqlSugar] 周报：快照 → MySQL 行类型 → Insertable
    private static async Task ApplyWeeklyReportAsync(SqlSugarClient database, LocalOperationChange change)
    {
        var report = SqliteOperationPayload.DeserializeWeeklyReport(change.AfterJson!);
        var existing = await database.Ado.SqlQueryAsync<string>(
            "SELECT id FROM weekly_reports WHERE id = @id OR week_start = @weekStart FOR UPDATE",
            new
            {
                id = report.Id.ToString("D"),
                weekStart = report.Week.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
        if (existing.Count > 0)
        {
            throw new InvalidOperationException("MySQL 已存在相同周报主键或周起始日期，拒绝覆盖。");
        }

        await database.Insertable(new WeeklyReportRow
        {
            Id = report.Id.ToString("D"),
            WeekStart = report.Week.Start.ToDateTime(TimeOnly.MinValue),
            WeekEnd = report.Week.EndInclusive.ToDateTime(TimeOnly.MinValue),
            WorkSummary = report.WorkSummary,
            NextWeekPlan = report.NextWeekPlan,
            Risks = report.Risks,
            ConfirmedMinutes = report.ConfirmedMinutes,
            EstimatedMinutes = report.EstimatedMinutes,
            UnknownHoursCount = report.UnknownHoursCount,
            CreatedAtUtc = report.CreatedAtUtc.UtcDateTime,
            UpdatedAtUtc = report.UpdatedAtUtc.UtcDateTime
        }).ExecuteCommandAsync();
    }

    // [重构-统一SqlSugar] Git 项目：快照 → MySQL 行类型 → Insertable
    private static async Task ApplyGitProjectAsync(SqlSugarClient database, LocalOperationChange change)
    {
        var project = SqliteOperationPayload.Deserialize<GitProject>(change.AfterJson!);
        var existing = await database.Ado.SqlQueryAsync<string>(
            "SELECT id FROM git_projects WHERE id = @id FOR UPDATE",
            new { id = project.Id.ToString("D") });
        if (existing.Count > 0)
        {
            throw new InvalidOperationException("MySQL 已存在相同主键的 Git 项目，拒绝覆盖。");
        }

        await database.Insertable(new GitProjectRow
        {
            Id = project.Id.ToString("D"),
            Name = project.Name,
            RepositoryPath = project.RepositoryPath,
            AuthorField = project.AuthorField?.ToString(),
            Author = project.Author
        }).ExecuteCommandAsync();
    }

    // [重构-统一SqlSugar] Git 工作项 + 提交链接：快照 → MySQL 行类型 → Insertable（含 FK 项目保障）
    private static async Task ApplyGitWorkItemAsync(SqlSugarClient database, LocalOperationChange change)
    {
        var snapshot = SqliteOperationPayload.DeserializeGitWorkItem(change.AfterJson!);
        var item = snapshot.WorkItem.ToDomain();
        var project = snapshot.Project;
        var id = item.Id.ToString("D");

        var existing = await database.Ado.SqlQueryAsync<string>(
            "SELECT id FROM git_work_items WHERE id = @id OR (project_id = @projectId AND work_date = @workDate) FOR UPDATE",
            new
            {
                id,
                projectId = item.ProjectId.ToString("D"),
                workDate = item.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
        if (existing.Count > 0)
        {
            throw new InvalidOperationException("MySQL 已存在相同主键或项目日期的 Git 工作项，拒绝覆盖。");
        }

        // 确保 MySQL 侧项目存在（FK 约束）
        var projectCount = await database.Ado.SqlQueryAsync<long>(
            "SELECT COUNT(*) FROM git_projects WHERE id = @projectId",
            new { projectId = project.Id.ToString("D") });
        if (projectCount.Count == 0 || projectCount[0] == 0)
        {
            await database.Insertable(new GitProjectRow
            {
                Id = project.Id.ToString("D"),
                Name = project.Name,
                RepositoryPath = project.RepositoryPath,
                AuthorField = project.AuthorField?.ToString(),
                Author = project.Author
            }).ExecuteCommandAsync();
        }

        await database.Insertable(new GitWorkItemRow
        {
            Id = id,
            ProjectId = item.ProjectId.ToString("D"),
            WorkDate = item.WorkDate.ToDateTime(TimeOnly.MinValue),
            Description = item.Description,
            EstimatedMinutes = item.EstimatedMinutes,
            ConfirmedMinutes = item.ConfirmedMinutes,
            IsDeleted = item.IsDeleted,
            Revision = item.Revision
        }).ExecuteCommandAsync();

        foreach (var commit in item.Commits)
        {
            await database.Insertable(new GitCommitRow
            {
                ProjectId = commit.ProjectId.ToString("D"),
                CommitHash = commit.Hash,
                WorkItemId = id,
                AuthorName = commit.AuthorName,
                AuthorEmail = commit.AuthorEmail,
                AuthoredAt = commit.AuthoredAt.ToString("O", CultureInfo.InvariantCulture),
                Subject = commit.Subject
            }).ExecuteCommandAsync();
        }
    }

    // —— 队列管理（SQLite 侧，raw SqliteConnection 合理：仅读写自身元数据，无跨库类型转换）——

    private sealed record PendingOperation(
        string OpGroupId, string OperationKind, string ChangesJson, int PayloadVersion,
        string? DependsOn, string ContentHash);

    private async Task<List<PendingOperation>> LoadPendingOperationsAsync(CancellationToken ct)
    {
        await SqliteDatabase.EnsureInitializedAsync(databasePath);
        await using var connection = SqliteDatabase.Open(databasePath);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT op_group_id, operation_kind, changes_json, payload_version, depends_on, content_hash " +
            "FROM sync_operation WHERE status = 'Pending' ORDER BY created_at_utc, op_group_id";
        var result = new List<PendingOperation>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new PendingOperation(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5)));
        }
        return result;
    }

    private async Task MarkSyncedAsync(string opGroupId, CancellationToken ct)
    {
        await using var connection = SqliteDatabase.Open(databasePath);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE sync_operation SET status = 'Synced', attempted_at_utc = @at WHERE op_group_id = @id";
        command.Parameters.AddWithValue("@id", opGroupId);
        command.Parameters.AddWithValue("@at", SqliteTimestamp.FormatUtcNow());
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task MarkConflictAsync(PendingOperation operation, string reason, CancellationToken ct)
    {
        await using var connection = SqliteDatabase.Open(databasePath);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            var status = connection.CreateCommand();
            status.Transaction = transaction;
            status.CommandText = "UPDATE sync_operation SET status = 'Conflict', attempted_at_utc = @at WHERE op_group_id = @id";
            status.Parameters.AddWithValue("@id", operation.OpGroupId);
            status.Parameters.AddWithValue("@at", SqliteTimestamp.FormatUtcNow());
            await status.ExecuteNonQueryAsync(ct);

            var conflict = connection.CreateCommand();
            conflict.Transaction = transaction;
            conflict.CommandText =
                "INSERT OR REPLACE INTO sync_conflict (op_group_id, object_key, local_version, remote_version, " +
                "baseline_ref, reason, resolution_status, created_at_utc) VALUES (@group, @key, @local, @remote, @baseline, @reason, 'pending', @at)";
            conflict.Parameters.AddWithValue("@group", operation.OpGroupId);
            conflict.Parameters.AddWithValue("@key", operation.OperationKind);
            conflict.Parameters.AddWithValue("@local", operation.ChangesJson);
            conflict.Parameters.AddWithValue("@remote", "");
            conflict.Parameters.AddWithValue("@baseline", (object?)operation.DependsOn ?? DBNull.Value);
            conflict.Parameters.AddWithValue("@reason", reason);
            conflict.Parameters.AddWithValue("@at", SqliteTimestamp.FormatUtcNow());
            await conflict.ExecuteNonQueryAsync(ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task UpdateLastSyncTimeAsync(CancellationToken ct)
    {
        await using var connection = SqliteDatabase.Open(databasePath);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE local_meta SET last_sync_at_utc = @at WHERE id = 1";
        command.Parameters.AddWithValue("@at", SqliteTimestamp.FormatUtcNow());
        await command.ExecuteNonQueryAsync(ct);
    }
}
