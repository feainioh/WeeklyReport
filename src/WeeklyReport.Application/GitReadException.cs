namespace WeeklyReport.Application;

public sealed class GitReadException : Exception
{
    public GitReadException(string message) : base(message) { }
    public GitReadException(string message, Exception innerException) : base(message, innerException) { }
}
