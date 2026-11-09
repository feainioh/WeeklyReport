// [新增-SQLite单机] 《SQLite单机存储改造计划》阶段 0 定义、阶段 4 实现的完整工作库备份与恢复：
// 备份使用 SQLite 在线备份 API（sqlite3_backup，经 BackupDatabase）取得 WAL 一致快照——写入进行中也能拿到
// 事务一致的库副本；备份完成后执行 quick_check 与核心表存在性校验。
// 恢复流程：验证备份 → 生成恢复前一致快照 → 在关闭其他访问的前提下由 SQLite 事务性写入。
// 范围说明（计划阶段 0 第 3 项）：仅覆盖 SQLite 工作库（业务数据 + Git 本机绑定 + 历史同步记录 + 元数据）；
// 应用配置（database.json/deepseek.json）、受保护凭据与提醒状态存放在 %LOCALAPPDATA%\WeeklyReport 的独立
// 文件中，不属于数据库备份范围，需单独复制。
using Microsoft.Data.Sqlite;
using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Persistence.Sqlite;

public sealed class SqliteWorkLibraryBackup
{
    /// <summary>创建完整工作库备份（WAL 一致快照 + 完整性校验），不覆盖已有文件。</summary>
    /// <param name="databasePath">当前工作库路径（可为正在写入的库）。</param>
    /// <param name="backupPath">备份文件目标路径。</param>
    /// <param name="cancellationToken">取消备份。</param>
    public async Task CreateBackupAsync(
        string databasePath, string backupPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException("源工作库不存在，无法备份。");
        }

        var sourcePath = Path.GetFullPath(databasePath);
        var targetPath = Path.GetFullPath(backupPath);
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase)
            || new[] { "-wal", "-shm", "-journal" }.Any(suffix =>
                targetPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("备份路径不能是工作库或其伴生文件。");
        }

        var backupDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(backupDirectory))
        {
            Directory.CreateDirectory(backupDirectory);
        }

        if (new[] { "", "-wal", "-shm", "-journal" }.Any(suffix => File.Exists(targetPath + suffix)))
        {
            throw new InvalidOperationException("备份目标或其伴生文件已存在，请选择新的文件名。");
        }

        var temporaryPath = Path.Combine(backupDirectory!, $".weekly-report-backup-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                DefaultTimeout = 5,
                Pooling = false
            }.ToString()))
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = 5,
                Pooling = false
            }.ToString()))
            {
                await source.OpenAsync(cancellationToken);
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
            }

            await VerifyWorkLibraryAsync(temporaryPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (new[] { "-wal", "-shm", "-journal" }.Any(suffix => File.Exists(targetPath + suffix)))
            {
                throw new InvalidOperationException("备份目标的伴生文件已存在，请选择新的文件名。");
            }

            File.Move(temporaryPath, targetPath);
        }
        catch (SqliteException exception)
        {
            throw new DatabaseAccessException("工作库备份失败：源库不可读或目标路径不可写。", exception);
        }
        catch (IOException exception)
        {
            throw new DatabaseAccessException("工作库备份失败：目标路径不可写。", exception);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                File.Delete(temporaryPath + suffix);
            }
        }
    }

    /// <summary>离线恢复工作库，先保留一致快照，再由 SQLite 原子写入备份内容。</summary>
    /// <param name="backupPath">完整备份文件路径（CreateBackupAsync 产物）。</param>
    /// <param name="databasePath">要恢复到的当前工作库路径。</param>
    /// <param name="cancellationToken">取消恢复。</param>
    /// <returns>恢复前原库的副本路径（保留供人工核对，不自动删除）。</returns>
    public async Task<string> RestoreAsync(
        string backupPath, string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!File.Exists(backupPath))
        {
            throw new InvalidOperationException("备份文件不存在，无法恢复。");
        }

        var targetPath = Path.GetFullPath(databasePath);
        var sourcePath = Path.GetFullPath(backupPath);
        if (string.Equals(targetPath, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("备份文件不能与工作库相同。");
        }

        if (!File.Exists(targetPath))
        {
            throw new InvalidOperationException("当前工作库不存在，请先创建工作库后再恢复。");
        }

        // 调用方必须停止应用和所有外部数据库访问；SQLite 的备份写入在事务内提交或回滚。
        var preRestorePath = targetPath + $".pre-restore-{Guid.NewGuid():N}.bak";
        var stagedBackup = targetPath + $".restore-{Guid.NewGuid():N}.tmp";
        try
        {
            await CreateBackupAsync(sourcePath, stagedBackup, cancellationToken);
            await CreateBackupAsync(targetPath, preRestorePath, cancellationToken);
            SqliteConnection.ClearAllPools();
            // Detect already-open Windows handles; callers must prevent new external access as well.
            using (File.Open(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }

            using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = stagedBackup,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
            using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = targetPath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
            await source.OpenAsync(cancellationToken);
            await destination.OpenAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            source.BackupDatabase(destination);
            return preRestorePath;
        }
        catch (SqliteException exception)
        {
            throw new DatabaseAccessException(
                "恢复失败；请检查数据库占用情况，恢复前副本路径：" + preRestorePath, exception);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                File.Delete(stagedBackup + suffix);
            }
        }
    }

    /// <summary>验证一个 SQLite 文件是可读、完整且结构齐全的工作库（quick_check + 核心表存在）。</summary>
    private static async Task VerifyWorkLibraryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                DefaultTimeout = 5,
                Pooling = false
            }.ToString());
            await connection.OpenAsync(cancellationToken);

            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA quick_check";
                var result = await check.ExecuteScalarAsync(cancellationToken);
                if (!string.Equals(result?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DatabaseAccessException(
                        $"完整性校验未通过（quick_check = {result}）：{Path.GetFileName(path)}",
                        new InvalidOperationException($"quick_check returned: {result}"));
                }
            }

            using (var tables = connection.CreateCommand())
            {
                // 表名为固定常量，值经参数绑定传入（不拼接 SQL）
                tables.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN " +
                    "(@meta, @workEntries, @reports, @gitProjects, @gitWorkItems, @gitCommits, " +
                    "@syncOperation, @syncConflict, @migrationJournal)";
                tables.Parameters.AddWithValue("@meta", "local_meta");
                tables.Parameters.AddWithValue("@workEntries", "work_entries");
                tables.Parameters.AddWithValue("@reports", "weekly_reports");
                tables.Parameters.AddWithValue("@gitProjects", "git_projects");
                tables.Parameters.AddWithValue("@gitWorkItems", "git_work_items");
                tables.Parameters.AddWithValue("@gitCommits", "git_commit_links");
                tables.Parameters.AddWithValue("@syncOperation", "sync_operation");
                tables.Parameters.AddWithValue("@syncConflict", "sync_conflict");
                tables.Parameters.AddWithValue("@migrationJournal", "schemalite_version");
                var count = (long)(await tables.ExecuteScalarAsync(cancellationToken))!;
                if (count != 9)
                {
                    throw new DatabaseAccessException(
                        $"工作库结构不完整（核心表 {count}/9）：{Path.GetFileName(path)}",
                        new InvalidOperationException($"expected 9 core tables, found {count}"));
                }
            }
        }
        catch (SqliteException exception)
        {
            throw new DatabaseAccessException(
                "文件不是有效的 SQLite 工作库：" + Path.GetFileName(path), exception);
        }
    }
}
