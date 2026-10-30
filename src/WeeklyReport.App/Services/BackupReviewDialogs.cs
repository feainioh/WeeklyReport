using WeeklyReport.App.Views;
using WeeklyReport.Application;

namespace WeeklyReport.App.Services;

public sealed class BackupReviewDialogs : IBackupReviewDialogs
{
    public BackupConflictPolicy? ChooseImportPolicy(PreparedBackupImport import)
    {
        var window = new BackupImportReviewWindow(import);
        var owner = System.Windows.Application.Current.MainWindow;
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true ? window.Policy : null;
    }
}
