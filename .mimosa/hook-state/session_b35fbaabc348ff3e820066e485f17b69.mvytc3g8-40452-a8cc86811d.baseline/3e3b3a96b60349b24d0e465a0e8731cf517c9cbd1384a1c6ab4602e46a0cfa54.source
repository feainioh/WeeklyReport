using System.Windows;
using WeeklyReport.App.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace WeeklyReport.App.Views;

public partial class WeeklyReportsView : UserControl
{
    private readonly WeeklyReportsViewModel _viewModel;
    private Task? _initialization;

    public WeeklyReportsView(WeeklyReportsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await EnsureInitializedAsync();
    }

    public Task EnsureInitializedAsync() => _initialization ??= _viewModel.RefreshHistoryAsync();
}
