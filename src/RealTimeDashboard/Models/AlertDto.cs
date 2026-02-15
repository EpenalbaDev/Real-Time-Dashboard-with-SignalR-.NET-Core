namespace RealTimeDashboard.Models;

public sealed record AlertDto(
    string AlertId,
    string Message,
    AlertSeverity Severity,
    string? TransactionId,
    DateTimeOffset CreatedAt);

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}
