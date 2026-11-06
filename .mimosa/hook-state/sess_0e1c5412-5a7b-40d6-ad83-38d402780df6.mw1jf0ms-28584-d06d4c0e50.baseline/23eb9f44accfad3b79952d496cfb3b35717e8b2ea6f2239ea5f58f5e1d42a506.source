using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Backup;

namespace WeeklyReport.IntegrationTests;

public class BackupFileServiceTests
{
    [Fact]
    public async Task Export_DoesNotOverwriteWithoutConfirmation()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "backup.json");
        try
        {
            await File.WriteAllTextAsync(path, "original");
            var service = CreateService();

            await Assert.ThrowsAsync<IOException>(
                () => service.ExportAsync(path, BackupFileFormat.Json, BackupCodecTests.SampleBackup(), overwrite: false));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.EnumerateFiles(directory));

            await service.ExportAsync(path, BackupFileFormat.Json, BackupCodecTests.SampleBackup(), overwrite: true);
            var restored = await service.ImportAsync(path, BackupFileFormat.Json);
            Assert.Single(restored.Reports);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task FailedExcelExport_PreservesExistingFileAndRemovesTemporaryFile()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "backup.xlsx");
        try
        {
            await File.WriteAllTextAsync(path, "original");
            var source = BackupCodecTests.SampleBackup();
            source = source with
            {
                Reports = [source.Reports[0] with { WorkSummary = new string('a', ExcelBackupCodec.MaximumCellCharacters + 1) }]
            };

            await Assert.ThrowsAsync<BackupFormatException>(
                () => CreateService().ExportAsync(path, BackupFileFormat.Excel, source, overwrite: true));

            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.EnumerateFiles(directory));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task CancelledExport_PreservesExistingFile()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "backup.json");
        try
        {
            await File.WriteAllTextAsync(path, "original");
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateService().ExportAsync(path, BackupFileFormat.Json, BackupCodecTests.SampleBackup(),
                    overwrite: true, cancellation.Token));

            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.EnumerateFiles(directory));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Markdown_IsReadableButCannotBeImported()
    {
        var directory = CreateTestDirectory();
        var path = Path.Combine(directory, "report.md");
        try
        {
            var service = CreateService();
            await service.ExportAsync(path, BackupFileFormat.Markdown, BackupCodecTests.SampleBackup(), overwrite: false);
            var text = await File.ReadAllTextAsync(path);

            Assert.Contains("# 周报：2026-10-26 至 2026-11-01", text);
            Assert.Contains("## 本周工作", text);
            Assert.Contains("@summary", text);
            await Assert.ThrowsAsync<ArgumentException>(() => service.ImportAsync(path, BackupFileFormat.Markdown));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task NetworkPaths_AreRejectedBeforeAccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().ImportAsync(@"\\example.invalid\share\backup.json", BackupFileFormat.Json));
    }

    private static BackupFileService CreateService() => new(new JsonBackupCodec(), new ExcelBackupCodec());

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WeeklyReport.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
