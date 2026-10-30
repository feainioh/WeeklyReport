using System.Diagnostics;
using System.Globalization;
using System.Text;
using WeeklyReport.Application;
using WeeklyReport.Infrastructure.Git;

namespace WeeklyReport.IntegrationTests;

public class GitCliCommitReaderTests
{
    [Fact]
    public async Task Read_FiltersExactAuthorAndLocalDateFromCurrentHead()
    {
        using var repository = new TemporaryGitRepository();
        await repository.InitializeAsync();
        var firstTime = new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.FromHours(8));
        await repository.CommitAsync("Alice", "alice@example.invalid", firstTime, "本地描述（中文）");
        await repository.CommitAsync("Alicia", "alice-extra@example.invalid", firstTime.AddMinutes(20), "其他作者");
        await repository.CommitAsync("Alice", "alice@example.invalid", firstTime.AddHours(1), string.Empty);
        var originalBranch = (await repository.RunAsync(["branch", "--show-current"])).Trim();
        if (originalBranch.Length is 0 || originalBranch.Any(
                static ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('.' or '_' or '-' or '/')))
        {
            throw new InvalidOperationException("测试分支名称包含意外字符，拒绝继续执行 Git 命令。");
        }
        await repository.RunAsync(["checkout", "--quiet", "-b", "unmerged-feature"]);
        await repository.CommitAsync("Alice", "alice@example.invalid", firstTime.AddHours(2), "未合并分支");
        await repository.RunAsync(["checkout", "--quiet", originalBranch]);
        var headBefore = (await repository.RunAsync(["rev-parse", "HEAD"])).Trim();
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("UTC+08", TimeSpan.FromHours(8), "UTC+08", "UTC+08");
        var projectId = Guid.NewGuid();
        var query = new GitRepositoryQuery(
            projectId, repository.RepoPath, GitAuthorField.Email, "ALICE@EXAMPLE.INVALID",
            new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 30), timeZone);

        var result = await new GitCliCommitReader(repository.GitExecutable).ReadAsync(query);

        Assert.True(result.HasHead);
        Assert.False(result.IsShallow);
        Assert.Equal(2, result.Commits.Count);
        Assert.Equal("本地描述（中文）", result.Commits[0].Subject);
        Assert.Equal(string.Empty, result.Commits[1].Subject);
        Assert.All(result.Commits, commit => Assert.Equal(projectId, commit.ProjectId));
        Assert.All(result.Commits, commit => Assert.Equal(40, commit.Hash.Length));
        Assert.Equal(firstTime, result.Commits[0].AuthoredAt);
        Assert.Equal(headBefore, (await repository.RunAsync(["rev-parse", "HEAD"])).Trim());
        Assert.Equal(string.Empty, (await repository.RunAsync(["status", "--porcelain"])).Trim());

        var byName = new GitRepositoryQuery(
            projectId, repository.RepoPath, GitAuthorField.Name, "alice",
            query.Start, query.End, timeZone);
        Assert.Equal(2, (await new GitCliCommitReader(repository.GitExecutable).ReadAsync(byName)).Commits.Count);
    }

    [Fact]
    public async Task Read_EmptyRepositoryReportsNoHead()
    {
        using var repository = new TemporaryGitRepository();
        await repository.InitializeAsync();

        var result = await new GitCliCommitReader(repository.GitExecutable).ReadAsync(Query(repository.RepoPath));

        Assert.False(result.HasHead);
        Assert.False(result.IsShallow);
        Assert.Empty(result.Commits);
    }

    [Fact]
    public async Task Read_NonRepositoryReportsAnExplicitError()
    {
        using var repository = new TemporaryGitRepository();

        var exception = await Assert.ThrowsAsync<GitReadException>(
            () => new GitCliCommitReader(repository.GitExecutable).ReadAsync(Query(repository.RepoPath)));

        Assert.Contains("Git 仓库", exception.Message);
    }

    [Fact]
    public async Task Read_PreCancelledRequestDoesNotStartGit()
    {
        using var repository = new TemporaryGitRepository();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new GitCliCommitReader(repository.GitExecutable).ReadAsync(Query(repository.RepoPath), cancellation.Token));
    }

    [Fact]
    public async Task Read_TimeoutReportsAnExplicitErrorAndCleansUp()
    {
        using var repository = new TemporaryGitRepository();
        await repository.InitializeAsync();
        var reader = new GitCliCommitReader(repository.GitExecutable, TimeSpan.FromTicks(1));

        var exception = await Assert.ThrowsAsync<GitReadException>(() => reader.ReadAsync(Query(repository.RepoPath)));

        Assert.Contains("超时", exception.Message);
    }

    [Fact]
    public async Task Read_ShallowRepositoryIsExplicitlyMarked()
    {
        using var repository = new TemporaryGitRepository();
        await repository.InitializeAsync();
        var time = new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
        await repository.CommitAsync("Alice", "alice@example.invalid", time, "first");
        await repository.CommitAsync("Alice", "alice@example.invalid", time.AddHours(1), "second");
        var clonePath = await repository.CreateShallowCloneAsync();

        var result = await new GitCliCommitReader(repository.GitExecutable).ReadAsync(Query(clonePath));

        Assert.True(result.HasHead);
        Assert.True(result.IsShallow);
        Assert.Single(result.Commits);
        Assert.Equal("second", result.Commits[0].Subject);
    }

    private static GitRepositoryQuery Query(string path) => new(
        Guid.NewGuid(), path, GitAuthorField.Email, "alice@example.invalid",
        new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 30), TimeZoneInfo.Utc);

    private sealed class TemporaryGitRepository : IDisposable
    {
        private readonly string _root;
        private readonly string _hooks;

        public TemporaryGitRepository()
        {
            GitExecutable = GitExecutableLocator.Find();
            var executableName = Path.GetFileName(GitExecutable);
            if (!Path.IsPathFullyQualified(GitExecutable)
                || (OperatingSystem.IsWindows() ? executableName != "git.exe" : executableName != "git"))
            {
                throw new InvalidOperationException("测试 Git 可执行文件路径异常，拒绝启动。");
            }
            _root = Path.Combine(Path.GetTempPath(), "WeeklyReport.GitTests", Guid.NewGuid().ToString("N"));
            if (Directory.Exists(_root))
            {
                throw new IOException("测试目录已存在。");
            }

            RepoPath = Path.Combine(_root, "仓库 空格测试");
            _hooks = Path.Combine(_root, "empty-hooks");
            Directory.CreateDirectory(RepoPath);
            Directory.CreateDirectory(_hooks);
        }

        public string GitExecutable { get; }
        public string RepoPath { get; }

        internal Task<string> InitializeAsync() =>
            RunAsync(["-c", "init.templateDir=", "init", "--quiet"]);

        private static readonly HashSet<string> AllowedGitEnvironmentKeys = new(StringComparer.Ordinal)
        {
            "GIT_AUTHOR_NAME",
            "GIT_AUTHOR_EMAIL",
            "GIT_AUTHOR_DATE",
            "GIT_COMMITTER_NAME",
            "GIT_COMMITTER_EMAIL",
            "GIT_COMMITTER_DATE"
        };

        private static readonly HashSet<string> AllowedGitCommands = new(StringComparer.Ordinal)
        {
            "init", "commit", "clone", "checkout", "branch", "rev-parse", "status"
        };

        // 安全约束：测试辅助器只执行白名单内的 Git 子命令，跳过 `-c` 配置对后
        // 的首个非选项参数必须是允许的子命令，防止任意命令注入。
        private static void ValidateGitSubcommand(IReadOnlyList<string> arguments)
        {
            var index = 0;
            string? command = null;
            while (index < arguments.Count)
            {
                var argument = arguments[index];
                if (argument == "-c")
                {
                    index += 2;
                    continue;
                }

                if (argument.StartsWith('-'))
                {
                    index++;
                    continue;
                }

                command = argument;
                break;
            }

            if (command is null || !AllowedGitCommands.Contains(command))
            {
                throw new InvalidOperationException("测试辅助器仅允许执行白名单内的 Git 子命令。");
            }
        }

        internal Task<string> CommitAsync(string author, string email, DateTimeOffset authorTime, string subject) =>
            RunAsync(
                ["-c", "commit.gpgsign=false", "-c", $"core.hooksPath={_hooks}", "commit",
                    "--quiet", "--allow-empty", "--allow-empty-message", "-m", subject],
                new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_NAME"] = author,
                    ["GIT_AUTHOR_EMAIL"] = email,
                    ["GIT_AUTHOR_DATE"] = authorTime.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                    ["GIT_COMMITTER_NAME"] = "Test Committer",
                    ["GIT_COMMITTER_EMAIL"] = "committer@example.invalid",
                    ["GIT_COMMITTER_DATE"] = "2025-01-01T10:00:00+00:00"
                });

        internal async Task<string> CreateShallowCloneAsync()
        {
            var clone = Path.Combine(_root, "shallow-clone");
            await RunAsync(["-c", "init.templateDir=", "-c", "protocol.file.allow=always", "clone",
                "--quiet", "--depth=1", new Uri(RepoPath).AbsoluteUri, clone]);
            return clone;
        }

        internal async Task<string> RunAsync(
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            ValidateGitSubcommand(arguments);
            var info = new ProcessStartInfo
            {
                FileName = GitExecutable,
                WorkingDirectory = _root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            info.ArgumentList.Add("--no-pager");
            info.ArgumentList.Add("-C");
            info.ArgumentList.Add(RepoPath);
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            if (environment is not null)
            {
                foreach (var pair in environment)
                {
                    if (!AllowedGitEnvironmentKeys.Contains(pair.Key))
                    {
                        throw new InvalidOperationException("测试 Git 环境变量不在允许列表内。");
                    }

                    info.Environment[pair.Key] = pair.Value;
                }
            }

            using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动测试 Git。");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"测试 Git 命令失败：{await error}");
            }

            return await output;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                DeleteCreatedDirectory(_root);
            }
        }

        private static void DeleteCreatedDirectory(string directory)
        {
            var directoryAttributes = File.GetAttributes(directory);
            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(directory);
                return;
            }

            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    DeleteCreatedDirectory(path);
                }
                else
                {
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                    }

                    File.Delete(path);
                }
            }

            if ((directoryAttributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(directory, directoryAttributes & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(directory);
        }
    }
}
