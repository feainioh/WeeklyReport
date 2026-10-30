using System.Text;
using System.Windows;
using WeeklyReport.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace WeeklyReport.App.Views;

public partial class BackupImportReviewWindow : Window
{
    public BackupImportReviewWindow(PreparedBackupImport import)
    {
        InitializeComponent();
        var conflicts = import.Conflicts;
        var text = new StringBuilder();
        text.AppendLine($"项目：{import.Incoming.Projects.Count}，冲突：{conflicts.Projects}");
        text.AppendLine($"手工工作项：{import.Incoming.Entries.Count(item => item.Kind == BackupEntryKind.Manual)}，冲突：{conflicts.ManualEntries}");
        text.AppendLine($"Git 工作项：{import.Incoming.Entries.Count(item => item.Kind == BackupEntryKind.Git)}，冲突：{conflicts.GitWorkItems}");
        text.AppendLine($"Git 提交：{import.Incoming.Commits.Count}，冲突：{conflicts.GitCommits}");
        text.AppendLine($"周报：{import.Incoming.Reports.Count}，冲突：{conflicts.Reports}");
        if (conflicts.RestoredGitWorkItems > 0)
        {
            text.AppendLine($"覆盖将恢复 {conflicts.RestoredGitWorkItems} 条已删除 Git 工作项。");
        }

        if (import.Incoming.Entries.Any(item => item.Kind == BackupEntryKind.Git))
        {
            text.AppendLine();
            text.AppendLine("导入后，跨项目自动估算可能与现有项目重叠。请在 Git 工作项中按日期重算；已确认工时不会改变。");
        }

        text.AppendLine();
        text.AppendLine("新导入的项目不含本机路径和作者配置，需要重新绑定。覆盖不会修改现有项目的本机绑定。");
        SummaryText.Text = text.ToString();
    }

    public BackupConflictPolicy? Policy { get; private set; }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Policy = BackupConflictPolicy.Skip;
        DialogResult = true;
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "覆盖会替换冲突数据，且不可撤销。建议先导出备份。确定继续吗？", "确认覆盖",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Policy = BackupConflictPolicy.Overwrite;
        DialogResult = true;
    }
}
