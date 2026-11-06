# Phase 0 验证结论与入口清单

> 2026-11-03 执行。Spike 代码位于仓库外临时目录（`%TEMP%\wr-spike-sqlite`、`%TEMP%\wr-spike-sync`），按计划**不合并**；结论以本文件与 [ADR-01～05](./adr/) 为准。隔离库 `weeklyreport_test_spike` 仅含 spike 表。

## 1. Spike 结果汇总

| Spike | 项 | 结果 |
| --- | --- | --- |
| SQLite 兼容性（ADR-02） | schema 等价改写、GUID/日期/UTC 微秒/可空工时往返、布尔/软删除/修订号、外键（含复合）、唯一约束与自然键、多表事务回滚、触发器故障注入、WAL 并发 busy 等待、在线备份一致性、SqlSugar SQLite provider | 9/9 通过 |
| 自包含发布 | `win-x64 --self-contained`，发布产物直接运行，`e_sqlite3`/`SQLitePCLRaw` 随包加载 | 通过 |
| MySQL 同步闭环（ADR-01） | 同操作组重试幂等（响应丢失）、同 ID 哈希不一致拒绝、基线比较拒绝并发修改、业务失败回执不落库、锁等待超时后同 ID 重试成功 | 5/5 通过 |
| 故障注入能力 | MySQL 触发器（既有契约测试已用）、SQLite 触发器 `RAISE(ABORT)`、会话级锁超时、故障后重试路径 | 可用 |

**未验证项（诚实记录）**：真实进程崩溃注入未执行——"本地确认前崩溃"以"同 ID 重试幂等"等价覆盖，Phase 2 需补真实崩溃恢复测试；长期离线队列增长、多 Windows 用户并发为 Phase 2 测试范围。

## 2. 业务读写入口清单（接口清单）

### 写入口（全部需接 SQLite 路径 + 操作组入队）

| 入口 | 调用方 | 仓储操作 |
| --- | --- | --- |
| 工作记录 CRUD | `WorkEntriesViewModel` → `WorkEntryService` → `IWorkEntryRepository` | GetByWeek/Find/Add/Update/Delete |
| Git 项目绑定 | `GitProjectsViewModel` → `IGitProjectRepository` | List/Find/Add/Update/Delete（绑定为本机不导出数据） |
| Git 同步应用 | `GitSyncService` → `IGitSyncStore.ApplyAsync` | 多表事务组（项目/工作项/提交链接/工时） |
| Git 工作项编辑/软删除/重算 | `GitProjectsViewModel` → `IGitSyncStore` | UpdateWorkItem/SoftDeleteWorkItem/RecalculateDate |
| 周报保存/更新/删除 | `WeeklyReportsViewModel` → `WeeklyReportService` → `IWeeklyReportRepository` | FindByWeek/List/Add/Update(乐观并发)/Delete(乐观并发) |
| 备份导入 | `BackupViewModel` → `BackupService` → `IBackupStore.ImportAsync` | 多表事务组（见备份计划附录） |
| 数据库初始化/迁移 | `DatabaseSettingsViewModel` → `IDatabaseInitializer` | TestConnection/Initialize（DbUp） |

### 读入口（需切"当前有效视图"）

| 入口 | 调用方 | 说明 |
| --- | --- | --- |
| 周报草稿汇总 | `WeeklyReportDraftBuilder` | 读工作记录 + Git 工作项（`IGitReportWorkItemSource`） |
| 周选择列表（导出） | `BackupViewModel` → `IWeeklyReportRepository.ListAsync` | 与 CaptureAsync 并行的第二条读取路径 |
| 全量捕获 | `BackupService` → `IBackupStore.CaptureAsync` | JSON/Excel/Markdown 导出数据源 |
| Git 同步预览 | `GitSyncService` → `IGitSyncStore.LoadAllAsync` + 本机 Git 读取 | 预览含去重状态 |

### 不入业务镜像的持久化

提醒去重状态（`ProtectedReminderStateStore`，本地受保护文件）、数据库凭据（DPAPI）、DeepSeek 密钥（DPAPI）、悬浮窗偏好。

### 未保存编辑/并发保护现状

`CanDiscardChanges`（工作记录/Git/周报三处确认）、`IsBusy/CanInteract` 全 VM、`IsNavigationLocked`（备份导入锁导航）、周报仓储 `expectedUpdatedAtUtc` 乐观并发——Phase 1 切换数据源时必须全部保持。

## 3. Go/No-Go 建议

**GO（A1）**：ADR-01/02 的全部关键假设均被 spike 证实；未验证项均属 Phase 2 测试范围且有明确等价覆盖或计划。建议按计划进入 Phase 1（SQLite 完整业务能力），备份侧按《备份功能离线化改造计划》切片 2 并行接入。
