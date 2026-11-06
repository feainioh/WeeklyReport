# ADR-05 时间、版本与迁移规则

状态：已接受（Phase 0，2026-11-03）

## 决策

- **时间口径**：全链路 UTC；持久化精度统一为微秒（与 MySQL `DATETIME(6)`、`StorageDateRange.NormalizeUtcTimestamp` 一致）；SQLite 以 ISO8601 文本存储（`yyyy-MM-ddTHH:mm:ss.ffffffZ`，spike 验证往返无损）。日期字段（`work_date`/`week_start`）为 `yyyy-MM-dd` 文本。
- **版本三元组**：SQLite schema 版本（本地迁移）、MySQL 业务 schema 版本（现有 DbUp）、同步协议版本（`sync_receipt`/操作组格式）。三者独立演进；不兼容时暂停同步并提示，不自动降级。
- **迁移规则**：
  - SQLite：版本化迁移脚本，仅应用内执行，升级前自动备份工作副本（在线备份 API）；
  - MySQL：沿用 DbUp；同步协议表通过新的 DbUp 脚本引入；DDL 隐式提交，不承诺整体回滚（现状约束不变）；
  - 首次启用需用户确认后执行迁移；迁移失败保留原文件/原状态并明确报错。
- **基线与乐观并发**：镜像表增加 `revision`/`updated_at` 镜像列；周报沿用现有 `expectedUpdatedAtUtc` 乐观并发保护（MySQL 与 SQLite 同语义）。

## 依据

spike 验证微秒精度往返与迁移建表可行性（ADR-02）；现有 MySQL 侧 `DATETIME(6)` 与仓储乐观并发为既成事实。

## 补充（Phase 1 切片 A 实施，2026-11-03）

- [新增-离线存储] SQLite 侧时间戳映射实现口径：**不使用 SqlSugar 的 DateTime 映射**（其 SQLite 序列化仅保留毫秒，违反微秒要求）；行对象中日期/时间戳均为字符串属性，写入 `yyyy-MM-dd` / `yyyy-MM-ddTHH:mm:ss.ffffffZ` 固定格式（定宽可按字典序比较与排序），由仓储层负责与域类型的双向转换。契约测试已验证微秒往返与按周边界查询在 SQLite 侧与 MySQL 一致。
- SQLite 迁移由自研轻量执行器（`schemalite_version` 日记表 + 嵌入脚本单事务逐个应用）承载，不引入 DbUp.SQLite；MySQL 侧 DbUp 已加脚本范围过滤（`Persistence.Migrations`），两套迁移目录互不干扰。
