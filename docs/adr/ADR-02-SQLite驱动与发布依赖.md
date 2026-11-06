# ADR-02 SQLite 驱动与发布依赖

状态：已接受（Phase 0，2026-11-03）

## 决策

- **驱动栈：`Microsoft.Data.Sqlite` 10.0.11 + `SQLitePCLRaw`（e_sqlite3 原生组件）**。
- **仓储访问层沿用 SqlSugar**（`DbType.Sqlite`，版本 5.1.4.221 与现有 MySQL 侧一致）：spike 验证 SqlSugarCore 的 SQLite provider 在连接、参数化 SQL、事务（`UseTran`）下可用。
- **在线备份等底层操作直接使用 `Microsoft.Data.Sqlite`**（`SqliteConnection.BackupDatabase`）。
- 两套 API 共栈无冲突：SqlSugarCore 5.1.4.221 自身依赖 Microsoft.Data.Sqlite ≥ 10.0.11，同一原生组件。

## 依据（spike 证据，2026-11-03）

兼容性 9/9 通过（临时工程，未合并）：

- 真实业务 schema 手工等价改写后可建表；GUID(TEXT)/DateOnly(yyyy-MM-dd)/DateTimeOffset(ISO8601 微秒口径，与 `StorageDateRange.NormalizeUtcTimestamp` 一致)/可空工时往返无损；
- 布尔(0/1)、软删除、修订号 INTEGER 语义正确；
- 外键强制（含 `git_commit_links → git_work_items(id, project_id)` 复合外键）在 `PRAGMA foreign_keys=ON` 下生效；
- 周起始唯一、项目+日期唯一、复合主键等自然键约束生效；
- 多表事务失败整体回滚；触发器 `RAISE(ABORT)` 故障注入可用（与 MySQL 契约测试的触发器手法对等）；
- WAL 模式下第二连接写并发经 `DefaultTimeout`（busy_timeout）等待后成功；
- WAL 活动期间 `BackupDatabase` 生成的备份通过 `integrity_check` 且数据完整；
- `win-x64` 自包含发布（`dotnet publish -r win-x64 --self-contained`）后发布产物直接运行全部通过，`e_sqlite3.dll`/`SQLitePCLRaw.*` 正确随包。

## 后果与边界

- 连接管理须每次开启 `PRAGMA foreign_keys=ON`（SQLite 每连接独立）；WAL 模式在首次打开库时设置一次。
- MySQL `FOR UPDATE` 无 SQLite 等价物：以 `BEGIN IMMEDIATE` + busy_timeout 实现写互斥（spike 已验证互斥行为）。
- 排序规则差异（MySQL `utf8mb4_unicode_ci` 大小写不敏感 vs SQLite TEXT 二进制比较）：现有仓储 SQL 均按 id/日期/哈希等大小写无关值精确匹配、无 LIKE，风险低；Phase 1 契约测试须覆盖该差异项。
- 不共享原始 SQL：MySQL 迁移脚本不直接用于 SQLite，Phase 1 单独维护 SQLite schema 迁移。
