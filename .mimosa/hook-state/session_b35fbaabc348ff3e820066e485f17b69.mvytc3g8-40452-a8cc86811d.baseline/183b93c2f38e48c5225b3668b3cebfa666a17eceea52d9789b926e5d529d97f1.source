using WeeklyReport.App.Views;

namespace WeeklyReport.App.Services;

public sealed class AiReviewDialogs : IAiReviewDialogs
{
    public bool ConfirmSend(string model, string instruction, string content)
    {
        var details = $"模型：{model}{Environment.NewLine}" +
            $"服务：https://api.deepseek.com{Environment.NewLine}{Environment.NewLine}" +
            $"【系统指令】{Environment.NewLine}{instruction}{Environment.NewLine}{Environment.NewLine}" +
            $"【用户内容】{Environment.NewLine}{content}";
        return Show("确认发送到 DeepSeek", "以下内容将发送给 DeepSeek，可能产生费用。", details, "确认发送");
    }

    public bool ConfirmApply(string result) =>
        Show("预览 DeepSeek 结果", "请核对事实；确认后仅应用到编辑区，仍需手动保存。", result, "应用到编辑区");

    private static bool Show(string title, string description, string text, string confirmLabel)
    {
        var window = new AiReviewWindow(title, description, text, confirmLabel);
        var owner = System.Windows.Application.Current.MainWindow;
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true;
    }
}
