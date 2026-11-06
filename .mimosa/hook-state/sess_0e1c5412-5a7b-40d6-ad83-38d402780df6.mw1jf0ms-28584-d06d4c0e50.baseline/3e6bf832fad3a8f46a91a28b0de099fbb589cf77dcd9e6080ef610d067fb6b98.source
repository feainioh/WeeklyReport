using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using WeeklyReport.App.Services;
using WeeklyReport.Application;

namespace WeeklyReport.App.ViewModels;

public partial class AiSettingsViewModel(
    IAiSettingsStore store,
    IAiDraftService client,
    IUserDialogs dialogs,
    ILogger<AiSettingsViewModel> logger) : ObservableObject
{
    [ObservableProperty] private string _model = "deepseek-flash";
    [ObservableProperty] private string _statusMessage = "配置 API Key 后可手动使用 DeepSeek。";
    [ObservableProperty] private bool _isBusy;

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

            Model = settings.Model;
            StatusMessage = "已加载 DeepSeek 配置。";
            return settings.ApiKey;
        }
        catch (Exception exception) when (ErrorMessages.IsExpected(exception))
        {
            Report(exception);
            return null;
        }
    }

    public Task SaveAsync(string apiKey) => RunAsync(async () =>
    {
        await store.SaveAsync(new AiSettings(Model, apiKey));
        StatusMessage = "DeepSeek 配置已安全保存。";
    });

    public async Task<bool> DeleteAsync()
    {
        if (!dialogs.Confirm("确定删除本机保存的 DeepSeek 配置和 API Key？", "DeepSeek 设置"))
        {
            return false;
        }

        var deleted = false;
        await RunAsync(async () =>
        {
            await store.DeleteAsync();
            deleted = true;
            StatusMessage = "DeepSeek 配置已删除。";
        });
        return deleted;
    }

    public async Task TestAsync(string apiKey)
    {
        if (!dialogs.Confirm("连接测试会向 DeepSeek 发送固定的非敏感内容，可能产生费用。继续吗？", "DeepSeek 连接测试"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var request = new AiGenerationRequest(apiKey, Model, "只回复 OK。", "连接测试", userConfirmed: true);
            await client.GenerateAsync(request);
            StatusMessage = "DeepSeek 连接测试成功。";
        });
    }

    private async Task RunAsync(Func<Task> action)
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
            Report(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Report(Exception exception)
    {
        logger.LogWarning("DeepSeek settings operation failed: {ErrorType}", exception.GetType().Name);
        StatusMessage = ErrorMessages.Describe(exception);
    }
}
