using WeeklyReport.Application;

namespace WeeklyReport.App.Services;

public sealed class AiDraftCoordinator(
    IAiSettingsStore settingsStore,
    IAiDraftService client,
    IAiReviewDialogs dialogs)
{
    public async Task<string?> GenerateAsync(
        string instruction,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        var settings = await settingsStore.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException("尚未配置 DeepSeek，请先打开设置。");
        if (!dialogs.ConfirmSend(settings.Model, instruction, content))
        {
            return null;
        }

        var result = await client.GenerateAsync(
            new AiGenerationRequest(settings.ApiKey, settings.Model, instruction, content, userConfirmed: true),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return dialogs.ConfirmApply(result) ? result : null;
    }
}
