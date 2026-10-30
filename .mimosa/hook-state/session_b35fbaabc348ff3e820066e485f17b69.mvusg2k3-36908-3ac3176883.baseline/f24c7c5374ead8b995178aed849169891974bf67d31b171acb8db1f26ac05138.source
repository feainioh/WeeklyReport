using System.Windows;
using System.Windows.Controls;
using WeeklyReport.App.ViewModels;
using Forms = System.Windows.Forms;
using UserControl = System.Windows.Controls.UserControl;

namespace WeeklyReport.App.Views;

public partial class GitProjectsView : UserControl
{
    private readonly GitProjectsViewModel _viewModel;
    private bool _initialized;

    public GitProjectsView(GitProjectsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
        {
            _initialized = true;
            await _viewModel.RefreshAsync();
        }
    }

    private void BrowseRepository_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择本地 Git 仓库",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            _viewModel.RepositoryPath = dialog.SelectedPath;
        }
    }
}
