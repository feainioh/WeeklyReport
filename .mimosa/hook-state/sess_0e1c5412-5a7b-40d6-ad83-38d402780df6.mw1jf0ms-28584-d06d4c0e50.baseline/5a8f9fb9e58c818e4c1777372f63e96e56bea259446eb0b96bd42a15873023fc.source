using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeeklyReport.Application;

namespace WeeklyReport.App.Services;

public sealed class JsonScheduledTaskSettingsStore(
    string filePath,
    ILogger<JsonScheduledTaskSettingsStore> logger) : IScheduledTaskSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ScheduledTaskSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return ScheduledTaskSettings.Default;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var settings = JsonSerializer.Deserialize<ScheduledTaskSettings>(json, SerializerOptions);
            return Normalize(settings, json);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Scheduled task settings could not be loaded: {ErrorType}", exception.GetType().Name);
            return ScheduledTaskSettings.Default;
        }
    }

    public async Task SaveAsync(
        ScheduledTaskSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(settings, JsonSerializer.Serialize(settings, SerializerOptions));
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("无法确定定时任务配置目录。");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".scheduled-tasks-{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(normalized, SerializerOptions);
            await File.WriteAllTextAsync(temporary, json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static ScheduledTaskSettings Normalize(ScheduledTaskSettings? settings, string json)
    {
        settings ??= ScheduledTaskSettings.Default;
        var defaults = ScheduledTaskSettings.Default;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return defaults;
        }

        return settings with
        {
            GitDailyItems = NormalizeOptions(
                settings.GitDailyItems, defaults.GitDailyItems,
                document.RootElement, "gitDailyItems", requireWeekday: false),
            WeeklyDraft = NormalizeOptions(
                settings.WeeklyDraft, defaults.WeeklyDraft,
                document.RootElement, "weeklyDraft", requireWeekday: true)
        };
    }

    private static ScheduledTaskOptions NormalizeOptions(
        ScheduledTaskOptions? options,
        ScheduledTaskOptions defaults,
        JsonElement root,
        string propertyName,
        bool requireWeekday)
    {
        if (!root.TryGetProperty(propertyName, out var taskElement)
            || taskElement.ValueKind != JsonValueKind.Object
            || options is null)
        {
            return defaults;
        }

        var normalized = options with
        {
            Enabled = taskElement.TryGetProperty("enabled", out _) ? options.Enabled : defaults.Enabled,
            TimeOfDay = taskElement.TryGetProperty("timeOfDay", out _) ? options.TimeOfDay : defaults.TimeOfDay,
            Weekday = taskElement.TryGetProperty("weekday", out _) ? options.Weekday : defaults.Weekday,
            LastAttemptDate = taskElement.TryGetProperty("lastAttemptDate", out _)
                ? options.LastAttemptDate
                : defaults.LastAttemptDate,
            LastSuccessfulDate = taskElement.TryGetProperty("lastSuccessfulDate", out _)
                ? options.LastSuccessfulDate
                : defaults.LastSuccessfulDate,
            LastResult = taskElement.TryGetProperty("lastResult", out _)
                ? options.LastResult
                : defaults.LastResult
        };
        if (normalized.Weekday is DayOfWeek weekday && !Enum.IsDefined(weekday)
            || requireWeekday && normalized.Weekday is null)
        {
            return defaults;
        }

        if (!requireWeekday)
        {
            normalized = normalized with { Weekday = null };
        }

        return normalized with
        {
            LastResult = normalized.LastResult is { Length: > 500 } result ? result[..500] : normalized.LastResult
        };
    }
}
