namespace WeeklyReport.Application;

public enum AiFailureKind
{
    InvalidRequest,
    Authentication,
    InsufficientBalance,
    RateLimit,
    ServiceUnavailable,
    Network,
    Timeout,
    InvalidResponse
}

public sealed class AiServiceException : Exception
{
    public AiServiceException(AiFailureKind kind, string message) : base(message) => Kind = kind;

    public AiServiceException(AiFailureKind kind, string message, Exception innerException)
        : base(message, innerException) => Kind = kind;

    public AiFailureKind Kind { get; }
}
