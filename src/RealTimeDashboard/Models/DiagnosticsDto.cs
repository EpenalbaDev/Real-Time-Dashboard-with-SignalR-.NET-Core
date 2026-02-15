namespace RealTimeDashboard.Models;

public record DiagnosticsDto
{
    public required BroadcasterDiagnostics Broadcaster { get; init; }
    public required AggregatorDiagnostics Aggregator { get; init; }
    public required ProcessorDiagnostics Processor { get; init; }
    public int ActiveConnections { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

public record BroadcasterDiagnostics(
    long TotalBroadcasts,
    long TotalTransactionsBroadcast,
    double LastBroadcastMs,
    double MaxBroadcastMs,
    double AvgBroadcastMs);

public record AggregatorDiagnostics(
    long TotalComputations,
    double LastComputeMs,
    double MaxComputeMs,
    double AvgComputeMs);

public record ProcessorDiagnostics(
    long TotalTransactionsProduced,
    long TotalDbFlushes,
    long TotalDbRowsWritten,
    double LastFlushMs,
    double MaxFlushMs,
    double AvgFlushMs);
