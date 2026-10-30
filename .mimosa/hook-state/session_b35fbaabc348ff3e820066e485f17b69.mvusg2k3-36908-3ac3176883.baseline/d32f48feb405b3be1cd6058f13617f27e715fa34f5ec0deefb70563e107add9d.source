using WeeklyReport.Application;

namespace WeeklyReport.Infrastructure.Git;

public static class GitExecutableLocator
{
    public static string Find()
    {
        var executable = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = part.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)
                || (OperatingSystem.IsWindows() && directory.StartsWith(@"\\", StringComparison.Ordinal)))
            {
                continue;
            }

            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new GitReadException("未找到本机 Git，请安装 Git 并将其添加到 PATH。");
    }
}
