using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;
using ClosedXML.Excel;
using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Backup;

namespace WeeklyReport.IntegrationTests;

public class BackupCodecTests
{
    [Fact]
    public async Task Json_RoundTripsBusinessDataWithoutLocalConfiguration()
    {
        var source = SampleBackup();
        var codec = new JsonBackupCodec();
        using var stream = new MemoryStream();

        await codec.WriteAsync(source, stream);
        var json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RepositoryPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AuthorField", json, StringComparison.OrdinalIgnoreCase);
        stream.Position = 0;
        var restored = await codec.ReadAsync(stream);

        AssertEquivalent(source, restored);
    }

    [Fact]
    public async Task Json_RejectsUnknownAndDuplicateProperties()
    {
        var codec = new JsonBackupCodec();
        using var duplicate = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"FormatVersion":1,"FormatVersion":1}"""));
        using var unknown = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"FormatVersion":1,"ApiKey":"synthetic"}"""));

        await Assert.ThrowsAsync<BackupFormatException>(() => codec.ReadAsync(duplicate));
        await Assert.ThrowsAsync<BackupFormatException>(() => codec.ReadAsync(unknown));
    }

    [Fact]
    public async Task Excel_RoundTripsLiteralTextWithoutCreatingFormulas()
    {
        var source = SampleBackup();
        var codec = new ExcelBackupCodec();
        using var stream = new MemoryStream();

        await codec.WriteAsync(source, stream);
        stream.Position = 0;
        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheet("工作记录");
            Assert.False(sheet.Cell(2, 5).HasFormula);
            Assert.Equal("=2+2", sheet.Cell(2, 5).GetString());
            Assert.False(sheet.Cell(3, 5).HasFormula);
            Assert.Equal("'原始文本", sheet.Cell(3, 5).GetString());
            Assert.False(workbook.Worksheet("Git提交来源").Cell(2, 7).HasFormula);
            Assert.Equal("+synthetic", workbook.Worksheet("Git提交来源").Cell(2, 7).GetString());
        }

        stream.Position = 0;
        var restored = await codec.ReadAsync(stream);
        AssertEquivalent(source, restored);
    }

    [Fact]
    public async Task Excel_EnforcesExactCellLengthWithoutTruncation()
    {
        var source = SampleBackup();
        var maximum = source.Reports[0] with { WorkSummary = "'" + new string('a', ExcelBackupCodec.MaximumCellCharacters - 1) };
        var tooLong = maximum with { WorkSummary = maximum.WorkSummary + "a" };
        var codec = new ExcelBackupCodec();
        using var valid = new MemoryStream();
        await codec.WriteAsync(source with { Reports = [maximum] }, valid);
        valid.Position = 0;
        var restored = await codec.ReadAsync(valid);
        Assert.Equal(maximum.WorkSummary, Assert.Single(restored.Reports).WorkSummary);

        using var invalid = new MemoryStream();
        await Assert.ThrowsAsync<BackupFormatException>(
            () => codec.WriteAsync(source with { Reports = [tooLong] }, invalid));
        Assert.Equal(0, invalid.Length);
    }

    [Fact]
    public async Task Excel_RejectsFormulaCellsWithLocation()
    {
        var source = SampleBackup();
        var codec = new ExcelBackupCodec();
        using var original = new MemoryStream();
        await codec.WriteAsync(source, original);
        original.Position = 0;
        using var changed = new MemoryStream();
        using (var workbook = new XLWorkbook(original))
        {
            workbook.Worksheet("工作记录").Cell(2, 5).FormulaA1 = "=1+1";
            workbook.SaveAs(changed);
        }

        changed.Position = 0;
        var exception = await Assert.ThrowsAsync<BackupFormatException>(() => codec.ReadAsync(changed));
        Assert.Contains("工作记录", exception.Message);
        Assert.Contains("第 2 行第 5 列", exception.Message);
    }

    [Theory]
    [InlineData("xl/vbaProject.bin")]
    [InlineData("xl/externalLinks/externalLink1.xml")]
    public async Task Excel_RejectsMacrosAndExternalLinks(string entryName)
    {
        var codec = new ExcelBackupCodec();
        using var stream = new MemoryStream();
        await codec.WriteAsync(SampleBackup(), stream);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.CreateEntry(entryName);
        }

        stream.Position = 0;
        await Assert.ThrowsAsync<BackupFormatException>(() => codec.ReadAsync(stream));
    }

    [Fact]
    public async Task Excel_RejectsMissingSheetsAndHeaders()
    {
        var codec = new ExcelBackupCodec();
        using var original = new MemoryStream();
        await codec.WriteAsync(SampleBackup(), original);
        original.Position = 0;
        using var missing = new MemoryStream();
        using (var workbook = new XLWorkbook(original))
        {
            workbook.Worksheet("项目").Delete();
            workbook.SaveAs(missing);
        }

        missing.Position = 0;
        await Assert.ThrowsAsync<BackupFormatException>(() => codec.ReadAsync(missing));
    }

    [Fact]
    public async Task Excel_RejectsOversizedWorkbookBeforeReading()
    {
        var codec = new ExcelBackupCodec();
        using var source = new ClaimedLengthStream(ExcelBackupCodec.MaximumWorkbookBytes + 1L);

        var exception = await Assert.ThrowsAsync<BackupFormatException>(() => codec.ReadAsync(source));

        Assert.Contains("64 MB", exception.Message);
    }

    [Fact]
    public async Task Excel_AllowsTheSizeBoundaryToProceedToFormatValidation()
    {
        var codec = new ExcelBackupCodec();
        using var source = new ClaimedLengthStream(ExcelBackupCodec.MaximumWorkbookBytes);

        var exception = await Assert.ThrowsAsync<BackupFormatException>(() => codec.ReadAsync(source));

        Assert.DoesNotContain("64 MB", exception.Message);
        Assert.Contains("有效的 XLSX", exception.Message);
    }

    [Fact]
    public async Task Excel_RejectsAnArchiveClaimingTooMuchUncompressedData()
    {
        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("xl/workbook.xml");
            using var content = entry.Open();
            content.WriteByte(0);
        }

        var bytes = archiveStream.ToArray();
        var signature = new byte[] { 0x50, 0x4b, 0x01, 0x02 };
        var centralDirectory = bytes.AsSpan().IndexOf(signature);
        Assert.True(centralDirectory >= 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(centralDirectory + 24, 4), checked((uint)(ExcelBackupCodec.MaximumUncompressedBytes + 1)));
        using var source = new MemoryStream(bytes);

        var exception = await Assert.ThrowsAsync<BackupFormatException>(() => new ExcelBackupCodec().ReadAsync(source));

        Assert.Contains("256 MB", exception.Message);
    }

    internal static BackupDocument SampleBackup()
    {
        var now = new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
        var project = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var gitItem = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var manualItem = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var week = new DateOnly(2026, 10, 26);
        return new BackupDocument(
            1, now,
            [new BackupProject(project, "项目")],
            [
                new BackupEntry(manualItem, BackupEntryKind.Manual, null, week, "=2+2", null, 60, false, now, now),
                new BackupEntry(gitItem, BackupEntryKind.Git, project, week.AddDays(1), "'原始文本", 45, null, false, null, null)
            ],
            [new BackupCommit(project, gitItem, new string('a', 40), "name", "test@example.invalid", now, "+synthetic")],
            [new BackupReport(Guid.NewGuid(), week, week.AddDays(6), "@summary", "下周", "风险", 60, 45, 0, now, now)]);
    }

    private static void AssertEquivalent(BackupDocument expected, BackupDocument actual)
    {
        Assert.Equal(expected.FormatVersion, actual.FormatVersion);
        Assert.True(expected.ExportedAtUtc.EqualsExact(actual.ExportedAtUtc));
        Assert.Equal(expected.Projects.ToArray(), actual.Projects.ToArray());
        Assert.Equal(expected.Entries.ToArray(), actual.Entries.ToArray());
        Assert.Equal(expected.Commits.ToArray(), actual.Commits.ToArray());
        Assert.Equal(expected.Reports.ToArray(), actual.Reports.ToArray());
    }

    private sealed class ClaimedLengthStream(long length) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
