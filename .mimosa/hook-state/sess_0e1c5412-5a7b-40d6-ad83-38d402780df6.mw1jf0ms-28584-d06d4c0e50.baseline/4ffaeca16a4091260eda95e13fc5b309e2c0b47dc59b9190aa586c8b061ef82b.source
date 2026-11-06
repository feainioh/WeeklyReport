namespace WeeklyReport.Application;

public sealed class AiSettings
{
    public AiSettings(string model, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        Model = model.Trim();
        ApiKey = apiKey.Trim();
    }

    public string Model { get; }
    public string ApiKey { get; }

    public override string ToString() => "DeepSeek settings (credentials redacted)";
}
