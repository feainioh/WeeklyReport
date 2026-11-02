using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using WeeklyReport.App.Services;
using WeeklyReport.Application;

namespace WeeklyReport.App.ViewModels;

public partial class DatabaseSettingsViewModel(
    IDatabaseConfigurationStore store,
    IDatabaseInitializer initializer,
    IUserDialogs dialogs,
    ILogger<DatabaseSettingsViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    private string _host = "127.0.0.1";

    [ObservableProperty]
    private string _port = "3306";

    [ObservableProperty]
    private string _database = "weekly_report";

    [ObservableProperty]
    private string _userName = "root";

    [ObservableProperty]
    private string _statusMessage = "请先在本机 MySQL 中创建数据库，再测试连接并初始化。";

    [ObservableProperty]
    private bool _isBusy;

    public bool CanInteract => !IsBusy;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanInteract));

    public async Task<string?> LoadAsync()
    {
        try
        {
            var settings = await store.LoadAsync();
            if (settings is null)
            {
                return null;
            }

            Host = settings.Host;
            Port = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Database = settings.Database;
            UserName = settings.UserName;
            StatusMessage = "已加载本机数据库配置。";
            return settings.Password;
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            Report(exception);
            return null;
        }
    }

    public async Task TestAsync(string password)
    {
        await RunAsync(async () =>
        {
            await initializer.TestConnectionAsync(CreateSettings(password));
            StatusMessage = "数据库连接成功；尚未执行迁移。";
        });
    }

    public async Task<bool> InitializeAndSaveAsync(string password)
    {
        var settings = default(DatabaseConnectionSettings);
        try
        {
            settings = CreateSettings(password);
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            Report(exception);
            return false;
        }

        if (!dialogs.Confirm(
            $"将对本机数据库“{settings.Database}”执行版本化迁移并保存连接配置。请确认这是周报专用数据库。",
            "确认初始化数据库"))
        {
            return false;
        }

        var initialized = false;
        var saved = false;
        await RunAsync(async () =>
        {
            await initializer.InitializeAsync(settings);
            initialized = true;
            await store.SaveAsync(settings);
            saved = true;
            StatusMessage = "数据库初始化并保存配置成功。";
        }, () => initialized ? "数据库迁移已执行，但配置保存失败。" : null);
        return saved;
    }

    private DatabaseConnectionSettings CreateSettings(string password)
    {
        if (!int.TryParse(Port, out var port))
        {
            throw new ArgumentException("请输入有效的 MySQL 端口。");
        }

        return new DatabaseConnectionSettings(Host, port, Database, UserName, password);
    }

    private async Task RunAsync(Func<Task> action, Func<string?>? partialSuccess = null)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            Report(exception, partialSuccess?.Invoke());
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Report(Exception exception, string? prefix = null)
    {
        logger.LogWarning("Database settings operation failed: {ErrorType}", exception.GetType().Name);
        var message = ErrorMessages.Describe(exception);
        StatusMessage = prefix is null ? message : $"{prefix} {message}";
    }
}
