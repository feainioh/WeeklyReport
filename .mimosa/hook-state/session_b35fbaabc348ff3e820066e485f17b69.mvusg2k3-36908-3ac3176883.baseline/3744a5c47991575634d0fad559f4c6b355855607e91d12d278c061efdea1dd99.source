using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using WeeklyReport.Application;

namespace WeeklyReport.App.Services;

internal static class ErrorMessages
{
    public static bool IsExpected(Exception exception) =>
        exception is DatabaseAccessException or GitReadException or AiServiceException or BackupFormatException
            or InvalidOperationException or ArgumentException or KeyNotFoundException
            or IOException or UnauthorizedAccessException or CryptographicException or JsonException or SecurityException
            or FormatException;

    public static string Describe(Exception exception) => exception switch
    {
        DatabaseAccessException error => error.Message,
        GitReadException error => error.Message,
        AiServiceException error => error.Message,
        BackupFormatException error => error.Message,
        KeyNotFoundException error => error.Message,
        ArgumentException error => error.Message,
        InvalidOperationException error when error.Message == "尚未配置本地 MySQL，请先打开设置。" => error.Message,
        InvalidOperationException error when error.Message == "系统时间早于记录更新时间，请检查系统时钟。" => error.Message,
        InvalidOperationException error when error.Message is
            "所选项目尚未绑定本地仓库和作者。"
            or "尚未配置 DeepSeek，请先打开设置。"
            or "Git 项目已发生变化，请重新预览。"
            or "Git 工作项已发生变化，请重新预览。"
            or "Git 工作项已发生变化，请重新加载。"
            or "存在已删除工作项，请明确选择跳过后再应用。"
            or "所选仓库包含不完整历史，请确认后再应用。" => error.Message,
        InvalidOperationException error when error.Message is
            "该周周报已存在，请打开已有周报。"
            or "数据库内容已发生变化，请重新预览导入。"
            or "系统时间早于周报更新时间，请检查系统时钟。"
            or "周报工时总计超出支持范围。" => error.Message,
        CryptographicException => "无法解密本地凭据，请使用当前 Windows 用户重新配置。",
        JsonException or InvalidDataException or FormatException => "本地配置或数据格式无效，请检查配置和数据库。",
        UnauthorizedAccessException or SecurityException => "没有访问本地配置或系统设置的权限。",
        IOException => "本地文件读写失败，请检查配置目录。",
        _ => "操作失败，请检查本地配置和数据库状态。"
    };
}
