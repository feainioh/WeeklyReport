using System.Text.Json;

namespace WeeklyReport.Infrastructure.Configuration;

internal static class AtomicJsonFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static async Task<T?> ReadAsync<T>(string filePath, CancellationToken cancellationToken) where T : class
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken)
                ?? throw new InvalidDataException("本地配置文件为空。");
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public static async Task WriteAsync<T>(string filePath, T value, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("无法确定本地配置目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
