# SQLite 单机存储改造计划

> **实施状态（2026-11-09）：** 已实现主要改造，并根据审计修正备份恢复和状态提示。维护命令的实际弹窗、权限故障及无 MySQL 网络访问仍需端到端验收，不能仅凭历史测试数量宣布全部完成。配套文档：《[SQLite单机切换指南](./SQLite单机切换指南.md)》。

## 1. 目标与范围

暂时停用 MySQL 业务存储和同步，应用运行时仅使用本机 SQLite。保留 MySQL 相关代码、迁移脚本与已有同步记录，但不在新版本的正常运行路径中调用它们。

- 本机 SQLite 是唯一业务数据源，不代表异地备份或跨设备同步。
- SQLite 不存在时允许创建空库；已有库损坏、不可读写或迁移失败时明确报错，不回退 MySQL，也不覆盖原库。
- 保存工作记录、周报、Git 数据和导入备份仍须保持本地事务原子性。
- 新版本运行期间不连接 MySQL，包括自动任务、设置操作、初始化、镜像和手动同步。
- 暂不删除 MySQL 代码、依赖或表；未来重新启用需要另行设计和验证，不承诺简单切回即可同步。

## 2. 改造前基础与影响

- [App.xaml.cs](../src/WeeklyReport.App/App.xaml.cs) 按 SQLite 状态选择本地或 MySQL 仓储；当前本地库不可用时存在回退 MySQL 的路径。
- [SqliteOperationQueue.cs](../src/WeeklyReport.Infrastructure/Persistence/Sqlite/SqliteOperationQueue.cs) 将 SQLite 业务变更和待同步操作组放在同一事务；单纯停止同步会持续积累待处理记录。
- [SqliteSyncCoordinator.cs](../src/WeeklyReport.Infrastructure/Persistence/Sqlite/SqliteSyncCoordinator.cs) 包含启动、周期和手动同步入口，隐藏按钮并不能阻止后台同步。
- [SqliteBackupStore.cs](../src/WeeklyReport.Infrastructure/Persistence/Sqlite/SqliteBackupStore.cs) 已支持 SQLite 业务备份，但现有 JSON/Excel 交换格式不包含全部本机绑定、队列及状态，不能视为完整工作库备份。
- 原[本地优先存储计划](./离线可用与本地优先存储计划.md)以 MySQL 同步为目标；实施本计划时应更新相关 ADR、说明和待办的适用状态，不把历史设计误认为当前产品承诺。

## 3. 数据保护与切换规则

### 3.1 升级前准备

- [x] 区分已有 SQLite 工作库、只有 MySQL 数据、新安装三种情形；检查 SQLite 文件及元数据，不能仅凭缺少 SQLite 文件就认定是新用户。
  - **实施记录：** 新增 `src/WeeklyReport.App/Services/SqliteOnlyStorageGate.cs`（决策器）+ `App.xaml.cs` 启动路由重写；测试 `SqliteOnlyStorageGateTests`（7 项，覆盖三情形 + 损坏/未初始化/MysqlOnly）。缺 SQLite 文件时读 database.json 判断是否配置过 MySQL（只读本地 JSON，不连接）。
- [x] 对只有 MySQL 数据的用户，在升级到 SQLite-only 版本**之前**，使用现有版本提供的镜像或数据导出流程进行一次性迁移；检查结果和业务数据后再切换。迁移与新版本运行时零 MySQL 访问的约束互不冲突。
  - **实施记录：** 迁移是升级前操作（旧版本完成）；新版本启动遇此情形弹窗指引回旧版迁移或由用户明确选择建空库。流程见《SQLite单机切换指南》§2/§3。
- [x] 迁移失败、MySQL 不可用或数据来源无法判断时，不静默宣称数据已迁移。保留源库，提示恢复旧版或使用已有备份导入；是否创建空库须由用户明确选择。
  - **实施记录：** Gate 的 MysqlDataWithoutLocalLibrary 分支不自动建库，Yes/No 明确确认后才调 `StartEmptyWorkspaceAsync`；database.json 读取失败同样按此路径处理（不静默）。
- [x] 如果 SQLite 与 MySQL 均有数据，不自动覆盖、合并或推断哪个更新。先分别备份，列出差异并由用户确定数据来源。
  - **实施记录：** MysqlOnly 状态（= 两侧均有数据的停用形态）启动即拦截报错，不读 MySQL 比对、不以本地镜像替代；镜像覆盖防护（`EnsureQueueDrainedAsync`）保留。指南 §3 给出人工核对流程。
- [x] 对现有 SQLite 的 `Pending`、`AwaitingConfirmation`、`Conflict`、`Failed` 等记录及身份元数据原样保留；不能自动删除、重放或标记为已同步。旧版在远端已提交但本地未确认的情况需要单独核对。
  - **实施记录：** 运行路径不再消费队列（协调器/应用器不启动）；设置页按"停用前遗留"如实展示计数（`MainWindow.DescribeLocalState`）。AwaitingConfirmation 的远端已提交情形在指南 §5 提示升级前核对。

### 3.2 回滚与未来重新启用

- [x] 切换前保留独立位置的可验证备份及旧版；切换后产生的新数据须先备份，不能靠直接切回旧版恢复同步。
  - **实施记录：** 流程要求写入指南 §2/§6；完整备份能力见阶段 0 第 3 项实施记录。
- [x] SQLite-only 期间不再生成同步操作组，因此旧队列和旧基线无法完整描述此后修改。未来恢复 MySQL 时必须先冻结写入、备份双方、比较数据并制定全量重建基线或明确的迁移方案，再决定是否处理历史队列；不得直接重启旧同步器。
  - **实施记录：** 指南 §6 明确五步前提；`SqliteOperationQueue.SetEnqueueEnabled`（按库路径的入队开关）是未来重启用承接点，生产路径从不注册。

## 4. 实施步骤

### 阶段 0：入口盘点与迁移保障

- [x] 盘点依赖注入、数据库设置、备份、启动初始化、自动任务及同步入口，记录全部 MySQL 调用路径。
  - **实施记录：** 入口清单见《SQLite单机切换指南》§7（含处置状态表）；实施后 `git grep` 验证 App/Application 层活代码零 MySQL 实现引用，协调器构造仅存注释。
- [x] 验证旧版镜像/导出覆盖的业务字段和 Git 本机绑定，补充切换指南；既有业务交换格式的缺失字段须明确告知。
  - **实施记录：** 指南 §4"业务导出 ≠ 完整备份"对照表（JSON/Excel 不含 Git 本机绑定与历史同步记录）。
- [x] 定义可验证的完整工作库备份与恢复流程：使用 SQLite 在线备份 API 取得 WAL 一致快照；恢复前验证、备份当前库，恢复失败保留原库并明确报告。应用配置、凭据及提醒数据若不属于数据库，单独说明范围。
  - **实施记录：** 使用 SQLite 在线备份 API 生成一致快照并校验；恢复前保存完整快照，由 SQLite 事务性写入，须退出应用并关闭第三方访问。操作步骤和范围见指南 §4。

**完成条件：** 迁移前提、数据来源选择、失败与回滚路径明确，不修改或覆盖用户既有数据。✅（指南 §2/§3/§6）

### 阶段 1：SQLite 唯一启动路由

- [x] 初始化并迁移 SQLite，所有业务仓储、Git 存储及备份仓储统一注册 SQLite 实现。
  - **实施记录：** `App.xaml.cs` DI 注册不再按 localFirst 路由，IWorkEntryRepository / IGitProjectRepository / IGitSyncStore / IWeeklyReportRepository / IBackupStore 固定 SQLite 实现；ILocalStorageStateProvider 始终注册。
- [x] 实现新安装初始化，并区分既有库不可用、路径缺失和需要迁移的情况；禁止创建空库掩盖损坏或迁移失败。
  - **实施记录：** `SqliteOnlyStorageGate` 六分支决策（UseExisting / CreateEmpty / MysqlData / Corrupted / Uninitialized / Disabled）；损坏、未初始化、MysqlOnly 三类直接阻断启动（报错 + 日志 + 退出）。
- [x] 移除本地异常时回退 MySQL 的路径；启动失败显示可理解的错误并记录不含敏感数据的诊断信息。
  - **实施记录：** 原"以 MySQL 模式运行"回退提示已移除（注释保留）；阻断路径经 `Log.Error` 记录决策类型与异常类型名，不含连接串/密码。

**完成条件：** 无 MySQL 配置即可启动和持久化；SQLite 故障不会连接 MySQL 或覆盖原文件。✅

### 阶段 2：停止同步并解耦事务

- [x] 停止创建、启动和调用同步协调器及同步应用器，包括自动与手动入口。
  - **实施记录：** `App.xaml.cs` 中 `StartLocalSyncAsync` 调用与方法体、协调器构造全部注释；`SyncNowAsync` 恒返回"已停用"；设置页同步按钮移除。协调器/应用器代码保留（无调用点），由 MySQL 集成测试继续覆盖。
- [x] 从本地业务写事务中分离远端 outbox：工作记录、周报、Git 写入和备份导入仍保持原子性，但不新增 MySQL 待同步记录。
  - **实施记录：** `SqliteOperationQueue` 入队改为按库路径显式注册（`SetEnqueueEnabled`），生产路径从不注册 → 零入队；Serializable 事务与回滚语义不变。契约测试更新：`SqliteOperationQueueTests`（提交零入队 / 无队列表业务照常提交 / 顺序操作零入队），保留机械测试按路径注册后验证旧语义。
- [x] 保留同步表与历史数据；调整状态提供者和业务消费者，避免历史冲突或暂停状态阻碍本地操作，也不把旧状态描述为正在同步。
  - **实施记录：** 同步表与历史行无任何删除/重放路径；`MainWindow.DescribeLocalState` 新口径把历史计数展示为"停用前遗留，不再自动处理"；仓储读写不受 Pending/Conflict/Suspended 状态影响（无阻断逻辑，验证于仓储契约测试）。
- [x] 针对写入中途失败验证整体回滚，并核对所有 SQLite 写入入口，避免只修改部分仓储。
  - **实施记录：** 回滚语义在 `SqliteOperationQueueTests.BusinessFailure_RollsBackBusiness` / `OperationConnection_EnforcesDeclaredForeignKeys` 与各仓储契约失败用例中验证；全部写入入口（工作记录/周报/项目/Git 同步/备份导入）共用同一队列会话。

**完成条件：** 业务操作成功后不新增远端队列；失败不产生部分本地业务数据；历史队列不变。✅

### 阶段 3：界面与文档

- [x] 移除或停用 MySQL 配置、初始化、建立镜像、回退 MySQL 和立即同步入口，同时检查其后台调用已不可达。
  - **实施记录：** `MainWindow.xaml` MySQL 配置区与离线可用四按钮整体注释（XAML↔code-behind 成对处理）；`DatabaseSettingsViewModel` DI 注册移除；`git grep` 验证运行路径零 MySQL 构造点（指南 §7）。
- [x] 存储状态显示本地 SQLite；说明数据保存在本机、不自动跨设备同步以及业务导出与完整备份的区别。
  - **实施记录：** 设置页展示历史同步状态；周进度将旧队列视为历史信息，不再产生等待同步或冲突建议。
- [x] 更新 README、相关存储/备份计划和 ADR：记录 SQLite-only 暂行决策、历史同步状态及重新启用前的基线要求。
  - **实施记录：** README 存储与备份章节更新；ADR-01 追加状态变更说明；《离线可用与本地优先存储计划》头部加"已被 SQLite-only 暂行决策取代"注记。

**完成条件：** 用户无法从新版本界面或常规后台流程触发 MySQL 操作，文档和实际行为一致。✅

### 阶段 4：备份恢复与回归验证

- [ ] 按阶段 0 的流程实现或核验完整 SQLite 备份恢复；WAL 写入期间验证备份一致性，恢复后检查完整性、迁移和业务读写。
  - **实施记录：** 自动测试覆盖活动 WAL 读取、恢复往返、目标占用及取消前保留；维护命令弹窗和实际故障恢复仍需端到端验收。
- [x] 验证现有 JSON/Excel 业务导入导出及 Markdown 导出，保留预览、冲突策略与失败回滚语义。
  - **实施记录：** 既有备份契约测试（BackupStoreContract 系列）与导入回滚用例全部通过（集成 119 项内）。
- [x] 覆盖新安装、既有 SQLite、只有 MySQL 数据、双份数据不一致、历史队列以及 SQLite 损坏、权限不足、迁移中断等场景。
  - **实施记录：** 新安装/既有/仅 MySQL/损坏/迁移中断/MysqlOnly = `SqliteOnlyStorageGateTests`；历史队列 = 安全测试 + `InitializeMirror_IsRejectedWhenQueueHasPendingOperations`；双份数据不一致 = 升级前流程（指南 §3，MysqlOnly 运行时拦截）。**遗留：权限不足场景未做专项自动化测试**（损坏路径已覆盖同类报错语义）。
- [x] 验证业务、提醒、周报和 Git 工作流；构建并运行受影响测试，必要时运行完整解决方案测试。
  - **实施记录：** 审计修复后运行解决方案测试：单元 136、桌面 151、集成 101 项通过；21 项 MySQL 真库测试未配置并跳过。
- [ ] 通过可观测的网络/调用测试验证正常运行、设置操作和后台任务均不连接 MySQL；不要仅以隐藏按钮作为证明。
  - **实施记录：** 证据链=①运行路径 `git grep` 零 MySQL 构造点（非隐藏，是删除注册与调用）；②DI 容器无任何 MySQL 实现；③`SqliteSyncSafetyTests` 以 NoDatabaseConfigurationStore 桩断言不同步场景不读取配置；④启动路由唯一 MySQL 相关读取是 database.json（本地文件）。**遗留：未做网络层抓包级验证**，如需更强证据可在隔离环境用防火墙规则复测。

**完成条件：** 测试与构建结果记录清楚，迁移/备份/恢复和无 MySQL 访问的关键验收通过。尚有上述验收待完成。

## 5. 验收清单

（2026-11-09 实施后状态）

1. ✅ 全新安装无需 MySQL 即可创建 SQLite 并在重启后保留数据。（Gate CreateEmptyLibrary 分支 + 仓储契约测试）
2. ✅ 既有 SQLite 的业务数据、Git 绑定与历史同步记录在升级后保留；普通本地写入及备份导入不新增待同步项。（队列零入队契约测试；历史队列无消费路径）
3. ✅ 仅有 MySQL 数据时，升级前迁移可验证且失败不丢失源数据；无法确认数据来源时不静默以空库替代。（MysqlData 分支用户明确选择；指南 §2/§3）
4. ✅ SQLite 不可用时明确报错，不回退、覆盖或连接 MySQL。（Corrupted/Uninitialized 阻断 + `CorruptDatabaseFile_ThrowsDatabaseAccessException`）
5. ⏳ 所有正常 UI、手动操作和后台任务均不访问 MySQL。（尚需可观测的网络/调用验收。）
6. ⏳ SQLite 事务、业务交换、完整备份恢复及主要业务流程的成功、失败和重启场景通过验证。（尚需维护命令端到端验收。）
7. ✅ 未来恢复同步的限制、备份范围和回滚要求均有明确说明。（指南 §4/§6）

## 6. 依赖和实施顺序

先完成数据保护与迁移规则，再进行启动路由、事务解耦和 UI 调整，最后实施完整备份恢复并开展端到端验收。完整备份恢复是本计划的验收内容，不应只以 JSON/Excel 导出替代。一次性 MySQL 迁移是升级前操作；新版本正常运行始终保持 SQLite-only。
