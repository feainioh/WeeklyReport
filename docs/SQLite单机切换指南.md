# SQLite 单机存储切换指南

> 配套《[SQLite单机存储改造计划](./SQLite单机存储改造计划.md)》。
> 面向从"MySQL / 本地优先"版本升级到 **SQLite-only** 版本的用户与维护者。

## 1. 版本行为差异

- 新版本运行期间**不连接 MySQL**：不再有数据库配置、初始化、建立镜像、回退 MySQL 和立即同步入口；后台定时任务与启动流程也不触达 MySQL。
- SQLite 工作库（`%LOCALAPPDATA%\WeeklyReport\weekly-report.db`）是唯一业务数据源，**不代表异地备份或跨设备同步**。
- 历史待同步/冲突记录**原样保留**但不再消费：设置页把它们展示为"停用前遗留"，不会再自动处理。

## 2. 升级前准备（按数据现状分三种情形）

| 情形 | 判定 | 升级前动作 |
| --- | --- | --- |
| 已有 SQLite 工作库（本地优先已启用） | SQLite 文件存在且状态为 Ready/Suspended | 无需迁移，直接升级。建议先用 §4 完整备份一次 |
| 只有 MySQL 数据 | 无 SQLite 文件，但配置过 database.json | **必须先在旧版本完成迁移**：设置 → 离线可用 → 「从 MySQL 建立本地镜像」；或导出 JSON 备份、升级后导入。确认镜像数据完整后再升级 |
| 新安装 | 无 SQLite 文件且从未配置 MySQL | 直接安装；首次启动自动创建空工作库 |

升级前通用要求：保留旧版安装包与一份**可验证的独立备份**（SQLite 完整备份或 MySQL dump）；切换后产生的新数据须先备份，不能靠直接切回旧版恢复同步。

## 3. 启动拦截与用户选择（新版本行为）

启动路由由 `SqliteOnlyStorageGate` 判定（`src/WeeklyReport.App/Services/SqliteOnlyStorageGate.cs`，测试见 `tests/WeeklyReport.DesktopTests/SqliteOnlyStorageGateTests.cs`）：

- **库损坏/不可读写**：明确报错并退出，不回退 MySQL、不覆盖原库、不建空库掩盖。
- **文件存在但未初始化**（迁移中断形态）：报错指引回旧版完成迁移或业务备份导入。
- **MysqlOnly 状态**（曾在旧版"停用并回退 MySQL"）：权威数据在 MySQL，报错指引回旧版重新启用本地优先并核对数据；不自动覆盖、合并或推断哪个更新。
- **无 SQLite 但配置过 MySQL**：弹窗说明并给出明确选择——回旧版迁移（推荐），或**用户明确确认**后建立空白工作库（不含 MySQL 历史数据）。
- **新安装**：自动创建空工作库。
- **数据来源无法判断**（如 database.json 不可读）：不静默宣称已迁移，按"只有 MySQL 数据"路径处理。

若 SQLite 与 MySQL 均有数据且可能不一致：不要在两边之间猜测——先分别完整备份，列出差异，由用户确定数据来源后再执行迁移。

## 4. 完整工作库备份与恢复

实现：[SqliteWorkLibraryBackup.cs](../src/WeeklyReport.Infrastructure/Persistence/Sqlite/SqliteWorkLibraryBackup.cs)；维护命令使用与桌面程序相同的单实例锁。

1. 从系统托盘**退出**周报程序，而不只是关闭窗口；同时关闭所有打开工作库的其他工具，直到维护操作完成。应用锁不能阻止第三方程序重新打开数据库。
2. 在 PowerShell 中进入发布程序所在目录，使用不存在的备份文件名执行：

   ```powershell
   .\WeeklyReport.App.exe --backup-work-library "D:\周报备份\weekly-report-2026-11-09.wrbak"
   ```

3. 恢复时，确保当前工作库已经存在，然后执行以下命令并确认弹窗：

   ```powershell
   .\WeeklyReport.App.exe --restore-work-library "D:\周报备份\weekly-report-2026-11-09.wrbak"
   ```

4. 等待成功或失败提示。恢复成功后重新启动应用并核对数据；恢复前副本的路径会在成功提示中显示。若目标工作库不存在，先正常启动应用创建空库并退出，再执行恢复。如果当前库损坏无法生成恢复前快照，命令会拒绝恢复；先将主库及可能存在的 `-wal`、`-shm` 文件一起复制到安全位置，再由维护者处理，不要直接删除原文件。

- **备份**：SQLite 在线备份取得 **WAL 一致快照**，校验通过后才发布到新文件；不覆盖现有文件。将备份保存在独立位置。
- **恢复**：先验证备份并以在线快照保存 `.pre-restore-*.bak` 副本，再由 SQLite 事务性写入；提交开始前支持取消，提交开始后不响应取消。若数据库被其他进程占用会拒绝恢复；执行期间仍须自行确保外部工具不重新访问。
- **范围**：仅覆盖 SQLite 工作库（业务数据 + Git 本机绑定 + 历史同步记录 + 身份元数据）。应用配置（database.json / deepseek.json）、受保护凭据、提醒去重状态、悬浮窗偏好存放在 `%LOCALAPPDATA%\WeeklyReport` 下的独立文件，需单独复制。

### 业务导出 ≠ 完整备份

| | JSON/Excel 导出（导入导出页） | 完整工作库备份 |
| --- | --- | --- |
| 工作记录/周报/项目 | ✓ | ✓ |
| Git 本机绑定（仓库路径、作者匹配） | ✗（导入后需重新绑定） | ✓ |
| 历史同步记录与身份元数据 | ✗ | ✓ |
| 跨设备/跨库迁移业务数据 | ✓（设计用途） | ✗（含本机特定绑定） |

## 5. 历史同步记录处理

- `Pending` / `AwaitingConfirmation` / `Conflict` / `Failed` 操作组与 `local_meta` 身份元数据在升级后**原样保留**，不删除、不重放、不标记为已同步。
- 特别注意 **AwaitingConfirmation**：旧版可能已把变更提交到 MySQL 但回执未确认（远端已提交、本地未确认）。这些记录停留在待确认状态；若未来需要核对远端，请在升级前用旧版执行一次"立即同步"并处理结果。
- SQLite-only 期间不再生成新的操作组，因此旧队列与旧基线无法描述此后的修改。

## 6. 回滚与未来重新启用 MySQL 同步

- **回滚到旧版**：先对新版本期间的 SQLite 数据做完整备份（§4），再安装旧版。旧版以 MySQL 模式运行时看不到 SQLite-only 期间的新数据。
- **重新启用同步的前提**（不承诺简单切回即可同步）：
  1. 冻结所有写入并退出应用；
  2. 分别备份 SQLite 工作库与 MySQL 库；
  3. 比较两侧数据并确定权威版本；
  4. 制定全量重建基线或明确的迁移方案（旧队列此后不可直接重放）；
  5. 不得直接重启旧同步器。`SqliteOperationQueue.SetEnqueueEnabled` 是未来重新启用的入队承接点（生产路径从不注册）。

## 7. MySQL 调用路径入口清单（实施时盘点，供审查）

运行路径已无 MySQL 触达（下述实现仅作为保留代码存在，`git grep` 可验证无活引用）：

| 入口 | 处置 |
| --- | --- |
| `App.xaml.cs` MySQL 模式仓储/备份/周报路由 | 已移除（统一 SQLite，旧代码注释保留） |
| `App.xaml.cs` `BuildLocalMirrorAsync`（读 MySQL 全量快照） | 已注释，设置页按钮移除 |
| `App.xaml.cs` `StartLocalSyncAsync` / `SyncNowAsync`（协调器启动/手动同步） | 启动调用与协调器构造已注释；手动入口返回"已停用" |
| `DatabaseSettingsViewModel` + MainWindow MySQL 设置区（测试连接/初始化） | DI 注册与 XAML 区域移除（注释保留） |
| `MySqlDatabaseInitializer` / `MySqlLocalMirrorSnapshotReader` / `MySqlSyncIdentityStore` / SqlSugar* 仓储 | 代码与迁移脚本保留，无调用点 |
| `SqliteSyncCoordinator` / `SqliteSyncApplier`（连 MySQL 写入） | 保留代码，App 不再构造；由 MySQL 集成测试继续覆盖 |
| `IDatabaseConfigurationStore`（database.json） | 仅供启动判定"MySQL 是否配置过"（读本地 JSON，不连接）与测试桩使用 |
| 依赖包 MySqlConnector / SqlSugarCore | 按计划暂不移除（保留代码可编译） |

后台任务（ScheduledGitSyncRunner / ScheduledWeeklyDraftRunner / GitSyncService）自身无 MySQL 引用，经仓储接口路由到 SQLite 实现；`SqliteSyncSafetyTests` 以 NoDatabaseConfigurationStore 桩验证不同步场景不读取数据库配置。

## 8. 验证说明

备份恢复和启动门禁设有针对性自动测试；维护命令的实际弹窗操作及第三方进程并发访问仍应在目标环境验收。历史测试数量不能代替当前版本的测试结果。
