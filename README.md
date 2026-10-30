# WeeklyReport

Windows 桌面周报工具，支持悬浮窗和系统托盘、本机 MySQL、Git 工作项与工时、DeepSeek 总结、定时提醒及数据导入导出。需求和阶段验收见[开发执行计划](./docs/开发执行计划.md)。

当前版本仍处于验收中：已验证隔离 MySQL（集成测试 63/63 通过）、备份文件往返、桌面主要业务、系统通知提交、本机升级安装与单实例运行；真实 DeepSeek 请求、通知实际显示与点击、干净 Windows 环境的完整验收尚未完成。

## 环境和安装

- Windows 10 版本 2004（内部版本 19041）或更新版本，x64。
- 本机 MySQL 8.0.46；需由用户自行安装、维护和启动。应用不会安装、升级或删除 MySQL。
- 使用 Git 工作项时，需要已安装 Git 并能从 `PATH` 调用 `git`。
- 构建源码需要 .NET 10 SDK；自包含发布目录和安装包不要求另行安装 .NET。

安装包默认安装到当前用户的 `%LOCALAPPDATA%\Programs\WeeklyReport`，无需管理员权限。安装包目前未签名，可能被 Windows 应用控制或组织策略阻止；请通过正常签名、审批或受信任开发环境处理，不要关闭或绕过安全策略。

首次使用前创建专用数据库，例如：

```sql
CREATE DATABASE weekly_report
    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

应用“设置”默认使用 `127.0.0.1:3306`、数据库 `weekly_report` 和用户名 `root`；密码需在本机界面输入，不写入源码或命令行。也可以改用专用账号：首次初始化需要执行建表等迁移的权限，日常访问需要相应的读写权限。先测试连接，再确认“初始化并保存”；迁移会修改所选数据库，请先确认目标及备份。

## 日常使用

1. 在“工作记录”中填写日期、内容及可选的已确认工时。
2. 在“Git 工作项”中为每个本地仓库配置项目名、路径和作者匹配方式，选择日期范围，先预览再确认同步。只读取所选仓库当前 `HEAD` 的提交，不会克隆、拉取或推送。
3. Git 提交按项目和本地日期合并为工作项。相邻提交间隔不超过 2 小时时才参与估算；跨项目重叠时段均分，自动估算每日最多 8 小时。估算是建议值，可手动修正并确认；无法推断不等同于零工时。
4. 在“周报”中选择日期，打开已有周报或生成草稿，编辑本周工作、下周计划和风险后保存。一周按周一至周日计算。
5. 关闭主窗口会隐藏到托盘；使用托盘菜单“退出”才会结束应用。Windows 登录时自启动默认关闭，可在“设置”中启用。

删除 Git 工作项时会清除描述、工时、作者及提交摘要，但保留已处理提交的身份和时间，防止下次同步静默重建。需要调整已有项目或日期的自动估算时，可在 Git 工作项界面手动重算；已确认工时不会被覆盖。

## 提醒

应用运行或在托盘后台运行时，按系统本地时间在每周四 17:00 和周五 10:00 分别提醒，即使该周周报已保存也会提醒。启动、系统时间或时区变化及唤醒后重新计算；错过不超过 2 小时的提醒可补发，同一周的同一提醒不重复发送。

应用退出、关机或休眠期间不保证准点提醒。提醒去重状态保存在当前 Windows 用户受保护的本地配置中，不依赖 MySQL；通知禁用、状态不可写或发送状态不确定时，界面会提示。若已手动检查不确定的提醒，可在设置中确认该状态；确认不代表通知已送达，也不会自动重发。系统接收通知不代表用户一定看到横幅，仍受通知权限和勿扰模式影响。主窗口的“测试提醒”只发送测试通知，不占用定时提醒的去重状态；点击通知可打开对应周报。

## DeepSeek

在“设置”中填写模型和自己的 DeepSeek API Key；默认模型为 `deepseek-flash`。可先使用连接测试，该测试会发送固定的非敏感内容，并可能产生费用。生成总结前会展示准备发送的数据和目标地址，只有用户确认后才发送至 `https://api.deepseek.com`；返回内容也需再次确认才应用到编辑器，仍需手动保存周报。

请自行判断工作内容是否允许发送给外部服务。API Key 使用当前 Windows 用户的 DPAPI 保护，不包含在备份中；删除配置可移除本机保存的密钥。真实服务的模型、额度和价格以 [DeepSeek 官方文档](https://api-docs.deepseek.com/)为准。

## 备份、导入和恢复

- JSON 导出全部业务数据；Excel 可选择一周或多周；Markdown 仅导出一份周报且不能导入。
- Excel 仅支持本应用导出或符合其模板的 `.xlsx`，不支持任意表格、`.xls`、宏、公式或外部链接。工作簿最多 64 MB，解压总量最多 256 MB，单个单元格最多 32,767 个字符；较大的业务数据可使用 JSON。
- 导入前会显示数量与冲突，默认可选择跳过；覆盖需再次确认，失败时业务导入事务回滚。导入后若 Git 数据与现有项目的时间重叠，请按需重算受影响日期的估算工时。
- 备份包含明文业务数据，请妥善保管；不包含数据库密码、API Key、提醒去重状态、本机仓库路径或作者匹配配置。新导入项目需重新绑定本地仓库；覆盖已有项目时保留其本机绑定。

建议同时使用 MySQL 官方工具定期备份数据库，并在独立测试库验证恢复。例如，确认 `D:\Backups` 已存在、备份账号权限合适后，可运行：

```powershell
mysqldump --host=127.0.0.1 --user=backup_user --password --single-transaction --routines --triggers --databases weekly_report --result-file="D:\Backups\weekly_report.sql"
```

`--password` 不带值时由工具交互式提示，勿把密码写进命令历史。恢复前先备份目标库，按 [MySQL 官方恢复文档](https://dev.mysql.com/doc/refman/8.0/en/reloading-sql-format-dumps.html)将备份载入隔离测试库并确认后，再由数据库管理员决定是否恢复正式数据。

## 构建、发布和测试

```powershell
dotnet restore WeeklyReport.slnx --locked-mode
dotnet build WeeklyReport.slnx --no-restore
dotnet test WeeklyReport.slnx --no-restore
dotnet run --project src\WeeklyReport.App\WeeklyReport.App.csproj

dotnet publish src\WeeklyReport.App\WeeklyReport.App.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o artifacts\publish\win-x64
ISCC.exe installer\WeeklyReport.iss
```

最后一条命令需要 Inno Setup 6；安装包生成在 `artifacts\installer`。升级前请从托盘退出应用；安装器会检查单实例互斥量，避免覆盖运行中的程序。升级安装会沿用之前选择的安装目录，新机器首次安装默认使用 `%LOCALAPPDATA%\Programs\WeeklyReport`。

普通测试不需要真实数据库。真实 MySQL 集成测试默认跳过；启用前需准备名称以 `weeklyreport_test_` 开头的独立本机测试数据库，并通过本机凭据对话框输入账号和密码：

```powershell
.\scripts\test-mysql.ps1 -Database weeklyreport_test_local
```

测试脚本不会创建或删除数据库，仅清理自身创建的记录，并在结束后恢复连接环境变量；不要将连接字符串或密码写入源码、日志或版本库。

## 卸载与故障排查

卸载前从托盘退出。卸载器尝试清理本应用的系统通知注册和指向当前安装路径的自启动项；清理失败会提示，静默卸载时可查看卸载日志。卸载保留 MySQL 数据库和 `%LOCALAPPDATA%\WeeklyReport` 下的配置、受保护密钥、提醒状态及日志；确认备份后可由用户自行删除不再需要的数据。

数据库连接失败时检查服务、端口、账号权限和专用库是否存在；通知不可用时检查 Windows 通知权限、勿扰模式及应用是否仍在运行；DeepSeek 失败时检查密钥、模型、网络与账户额度。日志位于 `%LOCALAPPDATA%\WeeklyReport\logs`，不应包含密码、API Key 或周报正文。
