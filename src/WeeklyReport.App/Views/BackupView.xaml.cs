using System.IO;
using System.Windows;
using WeeklyReport.App.ViewModels;
using WeeklyReport.Application;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using UserControl = System.Windows.Controls.UserControl;

namespace WeeklyReport.App.Views;

public partial class BackupView : UserControl
{
    private readonly BackupViewModel _viewModel;
    private readonly Func<bool> _canImport;
    private readonly Func<Task> _afterImport;

    public BackupView(BackupViewModel viewModel, Func<bool> canImport, Func<Task> afterImport)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _canImport = canImport;
        _afterImport = afterImport;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshWeeksAsync();
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync(BackupFileFormat.Json, "JSON 备份 (*.json)|*.json", ".json");

    private async void ExportExcel_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync(BackupFileFormat.Excel, "Excel 工作簿 (*.xlsx)|*.xlsx", ".xlsx");

    private async void ExportMarkdown_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync(BackupFileFormat.Markdown, "Markdown 文档 (*.md)|*.md", ".md");

    private async Task ExportAsync(BackupFileFormat format, string filter, string extension)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"周报-{DateTime.Today:yyyyMMdd}{extension}"
        };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.ExportAsync(dialog.FileName, format, File.Exists(dialog.FileName));
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!_canImport())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "周报备份 (*.json;*.xlsx)|*.json;*.xlsx|JSON 备份 (*.json)|*.json|Excel 工作簿 (*.xlsx)|*.xlsx",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var extension = Path.GetExtension(dialog.FileName);
        var format = extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? BackupFileFormat.Json
            : BackupFileFormat.Excel;
        if (await _viewModel.ImportAsync(dialog.FileName, format))
        {
            await _afterImport();
        }
    }
}
