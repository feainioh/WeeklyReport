// [新增-悬浮精灵] 悬浮窗样式枚举与偏好（位置 + 样式）读写：替代 FloatingWindow 内联持久化，
// 增加 Style 字段并向后兼容旧文件（缺省为卡片）；原子写与异常范围与原实现保持一致。
using System.IO;
using System.Text.Json;

namespace WeeklyReport.App.Services;

public enum FloatingStyle
{
    Card = 0,
    Sprite = 1
}

// [旧代码-机器人交互] 原偏好仅包含位置与样式
// public sealed record FloatingWindowPreferences(double Left, double Top, FloatingStyle Style = FloatingStyle.Card);

// [新增-机器人交互] 增加精灵行为设置；旧文件缺省时由 Store 归一化为默认值
public sealed record FloatingWindowPreferences(
    double Left,
    double Top,
    FloatingStyle Style = FloatingStyle.Card,
    SpriteBehaviorSettings? Sprite = null,
    WorkRhythmReminderSettings? WorkRhythm = null);

public static class FloatingWindowPreferencesStore
{
    /// <summary>读取偏好；文件缺失或内容无效时返回 null，由调用方决定提示策略。</summary>
    public static FloatingWindowPreferences? TryLoad(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var preferences = JsonSerializer.Deserialize<FloatingWindowPreferences>(json);
            if (preferences is null
                || !double.IsFinite(preferences.Left) || !double.IsFinite(preferences.Top)
                || Math.Abs(preferences.Left) > 100000 || Math.Abs(preferences.Top) > 100000
                || preferences.Style is not (FloatingStyle.Card or FloatingStyle.Sprite))
            {
                return null;
            }

            // [旧代码-机器人交互] 原样返回偏好
            // return preferences;

            // [新增-机器人交互] 归一化精灵行为设置：旧文件/未知档位回退默认值
            var sprite = preferences.Sprite;
            if (sprite is null || !Enum.IsDefined(sprite.Preset))
            {
                sprite = SpriteBehaviorSettings.Default;
            }
            else if (!HasDataAdviceSetting(json))
            {
                sprite = sprite with { DataAdvice = sprite.Preset != SpriteBehaviorPreset.Quiet };
            }

            if (!HasSpriteSetting(json, nameof(SpriteBehaviorSettings.AutoIdleActions)))
            {
                sprite = sprite with
                {
                    AutoIdleActions = sprite.Preset != SpriteBehaviorPreset.Quiet
                };
            }

            return preferences with
            {
                Sprite = sprite,
                WorkRhythm = preferences.WorkRhythm ?? WorkRhythmReminderSettings.Default
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool HasDataAdviceSetting(string json)
        => HasSpriteSetting(json, nameof(SpriteBehaviorSettings.DataAdvice));

    private static bool HasSpriteSetting(string json, string settingName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(nameof(FloatingWindowPreferences.Sprite), out var sprite)
            && sprite.ValueKind == JsonValueKind.Object
            && sprite.TryGetProperty(settingName, out _);
    }

    /// <summary>原子写入偏好；失败时抛出，由调用方按原有方式提示。</summary>
    public static void Save(string filePath, FloatingWindowPreferences preferences)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath))
            ?? throw new InvalidOperationException("无法确定悬浮窗配置目录。");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".floating-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences));
            File.Move(temporary, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
