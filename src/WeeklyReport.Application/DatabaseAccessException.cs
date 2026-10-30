namespace WeeklyReport.Application;

public sealed class DatabaseAccessException(string message, Exception innerException)
    : Exception(message, innerException);
