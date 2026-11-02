---
name: code-modification-convention
description: WeeklyReport 项目代码修改规范。修改本仓库任何现有源码文件（src/、tests/、installer/ 下的 .cs/.xaml/.csproj/.iss 等）时必须遵守：旧代码不删除、只注释保留并打标签；新增代码必须附带标注注释。凡涉及编辑已有文件的任务一律适用，即使用户没有再次提及本规范。
---

# 代码修改规范（旧代码注释保留制）

本项目采用"旧代码不删除、只注释保留"的修改方式，便于对照新旧实现与回溯。修改任何已有文件前先读本规范。

## 规则

1. **旧代码不删除，只注释**：替换或停用已有代码时，把原代码原地注释（C# 用 `//`，XAML 用 `<!-- -->`），不得直接删除。注释块前加标签行说明被停用的原因和所属变更。
2. **新增代码必须带标注注释**：每个新增代码块上方加一行标注注释，说明用途。全新文件在文件头加用途注释（此类文件无"旧代码"规则）。
3. **标签统一可检索**：同一次变更内使用统一标签格式 `[旧代码-<变更名>]` 与 `[新增-<变更名>]`，例如悬浮精灵改造统一用 `[旧代码-悬浮精灵]` / `[新增-悬浮精灵]`。这样 `git grep` 标签即可回溯一次变更的全部触点。
4. **被注释的旧代码不得再被引用**：XAML 中若事件处理器所依附的元素被注释，对应 code-behind 处理器一并注释；反之亦然，保证项目始终可编译、测试始终可运行。
5. **保持行为可验证**：完成修改后构建与相关测试必须通过；旧代码的注释不改变任何运行行为。

## XAML 注意事项

XAML 注释（`<!-- -->`）内部**不能出现连续两个连字符 `--`**。被注释的旧 XAML 若含 `--`（少见，如某些绑定表达式），需在注释内以单连字符或空格隔断，并在标签行注明"原样保留时已避免注释符冲突"。

## 示例

C#：

```csharp
public void SetReminderStatus(string message)
{
    // [旧代码-悬浮精灵] 卡片文本直写逻辑，现按样式路由到卡片/精灵内容
    // ReminderStatusText.Text = message;
    // ReminderStatusText.ToolTip = message;

    // [新增-悬浮精灵] 按当前样式路由提醒状态
    if (_style == FloatingStyle.Sprite)
    {
        _sprite?.SetReminderStatus(message);
    }
    else
    {
        _card?.SetReminderStatus(message);
    }
}
```

XAML：

```xml
<!-- [旧代码-悬浮精灵] 原卡片内容整体迁往 Controls/FloatingCard.xaml
<Border Background="{DynamicResource SurfaceBrush}" ...>
    ...
</Border>
-->
<!-- [新增-悬浮精灵] 按样式宿主卡片或精灵内容 -->
<ContentControl x:Name="FloatingContentHost" />
```

## 适用范围

- **必须遵守**：编辑本仓库已存在的源码文件（src/、tests/、installer/、scripts/ 下任何文本代码文件）。
- **不适用**：新建文件（只需文件头注释说明用途）；docs/ 文档；.gitignore 等配置的增行。
