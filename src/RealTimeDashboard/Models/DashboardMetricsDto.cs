namespace RealTimeDashboard.Models;

public sealed record DashboardMetricsDto
{
    public int TotalTransactions1Min { get; init; }
    public int TotalTransactions5Min { get; init; }
    public int TotalTransactions1Hour { get; init; }
    public decimal TotalVolume1Min { get; init; }
    public decimal TotalVolume5Min { get; init; }
    public decimal TotalVolume1Hour { get; init; }
    public double SuccessRate { get; init; }
    public double FailureRate { get; init; }
    public double AverageProcessingTimeMs { get; init; }
    public double TransactionsPerSecond { get; init; }
    public IReadOnlyList<SourceBreakdown> TopSources { get; init; } = [];
    public int FlaggedCount { get; init; }
    public int ActiveConnections { get; init; }
    public DateTimeOffset ComputedAt { get; init; }
}

public sealed record SourceBreakdown(string Source, int Count, decimal Volume);
