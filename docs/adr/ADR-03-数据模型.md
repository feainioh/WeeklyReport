# ADR-03 数据模型：镜像表、操作组、基线、冲突、数据库身份、协议版本

状态：已接受（Phase 0，2026-11-03；字段级定稿在 Phase 1 迁移实现时）

## 决策

### 本地业务库（SQLite，`%LOCALAPPDATA%\WeeklyReport` 独立文件）

- 镜像表与 MySQL 业务表一一对应（`work_entries`、`weekly_reports`、`git_projects`、`git_work_items`、`git_commit_links`），沿用相同业务主键（GUID）与自然键/唯一约束；类型映射按 ADR-02（GUID/日期/UTC 时间戳 TEXT、布尔与数值 INTEGER）。
- 元数据表：
  - `local_meta`：SQLite schema 版本、同步协议版本、镜像身份、最近完整镜像时间、存储状态、最近成功同步时间；
  - `sync_operation`：操作组 ID、业务事务类型、版本化语义变更集合（表、行主键、自然键、操作类型、本地变更前/后快照——非原始 SQL）、依赖组、状态（待同步/同步中/结果待确认/冲突/已同步/失败）、创建/尝试时间、内容哈希；
  - `sync_conflict`：操作组、对象、本地版本、MySQL 版本、基线、冲突原因、用户处理状态。
- 状态机沿用计划 §4.1/§4.5 定义；队列已同步组按保留策略清理，未同步/冲突组永不删除。

### MySQL 侧（同步协议表，Phase 2 迁移）

- `sync_receipt(op_group_id PK, content_hash, applied_at, result)`：业务写入与回执同 InnoDB 事务提交（spike 已验证原子性与幂等）。
- 数据库身份与协议版本记录表（具体名称 Phase 2 定）。

### 关键语义

- 同一业务事务的多表变更（周报保存、Git 同步组、备份导入）= 同一操作组；依赖组完成后才应用后续组。
- 手工记录/周报物理删除转可同步删除意图 + 基线；Git 工作项沿用软删除/提交去重语义。
- `sync_receipt` 的内容哈希即操作组内容哈希：同 ID 不同内容拒绝重放（spike 验证）。
- SQLite `sync_operation.payload_version` 独立版本化客户端载荷；v2 将操作快照、载荷版本纳入内容哈希，并与业务写入同一 SQLite 事务提交。v1 及无法反序列化/校验的记录保留为冲突，不从当前业务行补造历史快照。
- 自动同步只使用入队时的不可变新增快照；本地前/后快照不等同于已验证的 MySQL 基线，因此更新、删除在完成远端基线比较前继续作为冲突，不自动覆盖。

## 依据

spike（ADR-01/ADR-02）验证了 schema 等价性、复合唯一/外键、事务原子性、幂等回执与基线比较机制。

## 后果

- `BackupDocument` 保持纯交换模型，不承载镜像/队列（备份计划三项不变性之一）。
- ERD 即上述表结构；字段级 DDL 于 Phase 1/2 迁移脚本定稿。
