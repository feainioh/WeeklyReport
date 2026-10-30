using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using WeeklyReport.Application;
using WeeklyReport.Domain;

namespace WeeklyReport.Infrastructure.Git;

public sealed class GitCliCommitReader : IGitCommitReader
{
    private const int MaximumFieldLength = 1_000_000;
    private readonly string? _gitExecutable;
    private readonly TimeSpan _timeout;

    public GitCliCommitReader(string? gitExecutable = null, TimeSpan? timeout = null)
    {
        if (gitExecutable is not null && !Path.IsPathFullyQualified(gitExecutable))
        {
            throw new ArgumentException("Git 程序路径必须是绝对路径。", nameof(gitExecutable));
        }

        _gitExecutable = gitExecutable;
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
        if (_timeout <= TimeSpan.Zero || _timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<GitReadResult> ReadAsync(GitRepositoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(query.RepositoryPath))
        {
            throw new GitReadException("Git 仓库目录不存在或不可访问。");
        }

        var git = _gitExecutable ?? GitExecutableLocator.Find();
        var insideWorkTree = await RunTextAsync(
            git, query.RepositoryPath, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        if (insideWorkTree.ExitCode != 0 || !insideWorkTree.Output.Trim().Equals("true", StringComparison.Ordinal))
        {
            throw new GitReadException("请选择可访问且包含工作区的 Git 仓库。");
        }

        var shallow = await RunTextAsync(
            git, query.RepositoryPath, ["rev-parse", "--is-shallow-repository"], cancellationToken);
        if (shallow.ExitCode != 0 || shallow.Output.Trim() is not ("true" or "false"))
        {
            throw new GitReadException("无法确认 Git 仓库历史是否完整。");
        }

        var isShallow = shallow.Output.Trim() == "true";
        var head = await RunTextAsync(
            git, query.RepositoryPath, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken);
        if (head.ExitCode != 0)
        {
            var symbolic = await RunTextAsync(
                git, query.RepositoryPath, ["symbolic-ref", "--quiet", "HEAD"], cancellationToken);
            if (head.ExitCode == 1 && symbolic.ExitCode == 0
                && symbolic.Output.Trim().StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                return new GitReadResult([], HasHead: false, isShallow);
            }

            throw new GitReadException("无法读取当前 Git HEAD，请检查仓库状态。");
        }

        var commits = new List<GitCommit>();
        var log = await RunAsync(
            git,
            query.RepositoryPath,
            ["log", "--no-show-signature", "--no-color", "--no-decorate", "--no-patch", "--no-notes",
                "--no-ext-diff", "--no-textconv", "--encoding=UTF-8", "-z",
                "--format=tformat:%H%x00%an%x00%ae%x00%aI%x00%s", "HEAD", "--"],
            (reader, token) => ReadLogAsync(reader, query, commits, token),
            cancellationToken);
        if (log.ExitCode != 0)
        {
            throw new GitReadException("读取 Git 历史失败，请检查仓库权限、完整性和 Git 版本。");
        }

        return new GitReadResult(
            commits.OrderBy(commit => commit.AuthoredAt.UtcDateTime)
                .ThenBy(commit => commit.Hash, StringComparer.Ordinal)
                .ToArray(),
            HasHead: true,
            isShallow);
    }

    private async Task<ProcessResult<string>> RunTextAsync(
        string git, string directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        await RunAsync(
            git, directory, arguments,
            async (reader, token) => await reader.ReadToEndAsync(token),
            cancellationToken);

    private async Task<ProcessResult<T>> RunAsync<T>(
        string git,
        string directory,
        IReadOnlyList<string> arguments,
        Func<StreamReader, CancellationToken, Task<T>> readOutput,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(git, directory, arguments);
        using var process = new Process { StartInfo = startInfo };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        Task? errorReader = null;
        var started = false;

        try
        {
            if (!process.Start())
            {
                throw new GitReadException("无法启动本机 Git。");
            }

            started = true;
            errorReader = DrainAsync(process.StandardError);
            var output = await readOutput(process.StandardOutput, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await errorReader;
            return new ProcessResult<T>(process.ExitCode, output);
        }
        catch (Win32Exception exception)
        {
            throw new GitReadException("无法启动本机 Git，请检查程序路径和权限。", exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new GitReadException("Git 输出无法按 UTF-8 读取。", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitReadException("读取 Git 超时，请检查仓库状态后重试。", exception);
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // The Git process exited between the state check and termination.
                }

                await process.WaitForExitAsync(CancellationToken.None);
            }

            if (errorReader is not null)
            {
                await errorReader;
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string git, string directory, IReadOnlyList<string> arguments)
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var info = new ProcessStartInfo
        {
            FileName = git,
            WorkingDirectory = string.IsNullOrWhiteSpace(systemDirectory) ? AppContext.BaseDirectory : systemDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, throwOnInvalidBytes: true),
            StandardErrorEncoding = new UTF8Encoding(false, throwOnInvalidBytes: true)
        };
        foreach (var argument in new[]
        {
            "--no-pager", "-C", directory, "-c", "core.fsmonitor=false", "-c", "color.ui=false",
            "-c", "log.showSignature=false", "-c", "i18n.logOutputEncoding=UTF-8"
        })
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        info.Environment["LC_ALL"] = "C";
        return info;
    }

    private static async Task<bool> ReadLogAsync(
        StreamReader reader,
        GitRepositoryQuery query,
        ICollection<GitCommit> commits,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var field = new StringBuilder();
        var fields = new List<string>(5);
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                if (buffer[index] != '\0')
                {
                    if (field.Length >= MaximumFieldLength)
                    {
                        throw new GitReadException("Git 提交字段过长，无法安全读取。");
                    }

                    field.Append(buffer[index]);
                    continue;
                }

                fields.Add(field.ToString());
                field.Clear();
                if (fields.Count == 5)
                {
                    AddMatchingCommit(query, fields, commits);
                    fields.Clear();
                }
            }
        }

        if (field.Length != 0 || fields.Count != 0)
        {
            throw new GitReadException("Git 输出格式不完整，请检查仓库。");
        }

        return true;
    }

    private static void AddMatchingCommit(
        GitRepositoryQuery query,
        IReadOnlyList<string> fields,
        ICollection<GitCommit> commits)
    {
        var author = query.AuthorField == GitAuthorField.Name ? fields[1] : fields[2];
        if (!author.Trim().Equals(query.Author, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!DateTimeOffset.TryParse(
            fields[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out var authoredAt))
        {
            throw new GitReadException("Git 提交的作者时间无效。");
        }

        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(authoredAt, query.TimeZone).DateTime);
        if (localDate < query.Start || localDate > query.End)
        {
            return;
        }

        try
        {
            commits.Add(new GitCommit(query.ProjectId, fields[0], fields[1], fields[2], authoredAt, fields[4]));
        }
        catch (ArgumentException exception)
        {
            throw new GitReadException("Git 提交数据无效。", exception);
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        while (await reader.ReadAsync(buffer.AsMemory()) > 0)
        {
        }
    }

    private sealed record ProcessResult<T>(int ExitCode, T Output);
}
