namespace WeeklyReport.Application;

public sealed class AiGenerationRequest
{
    public AiGenerationRequest(
        string apiKey,
        string model,
        string instruction,
        string content,
        bool userConfirmed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (!userConfirmed)
        {
            throw new InvalidOperationException("发送至 DeepSeek 前必须由用户确认。");
        }

        ApiKey = apiKey.Trim();
        Model = model.Trim();
        Instruction = instruction;
        Content = content;
    }

    public string ApiKey { get; }
    public string Model { get; }
    public string Instruction { get; }
    public string Content { get; }

    public override string ToString() => "DeepSeek request (credentials and content redacted)";
}
