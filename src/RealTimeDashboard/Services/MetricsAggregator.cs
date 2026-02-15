using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RealTimeDashboard.Data;
using RealTimeDashboard.Data.Entities;
using RealTimeDashboard.Hubs;
using RealTimeDashboard.Models;

namespace RealTimeDashboard.Services;

public sealed class MetricsAggregator : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MetricsAggregator> _logger;

    public const string MetricsCacheKey = "dashboard:metrics";
    private static readonly TimeSpan ComputeInterval = TimeSpan.FromMilliseconds(500);

    // Performance counters
    private long _totalComputations;
    private double _lastComputeMs;
    private double _maxComputeMs;
    private double _avgComputeMs;

    public long TotalComputations => _totalComputations;
    public double LastComputeMs => _lastComputeMs;
    public double MaxComputeMs => _maxComputeMs;
    public double AvgComputeMs => _avgComputeMs;

    public MetricsAggregator(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<MetricsAggregator> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MetricsAggregator starting");

        var sw = new Stopwatch();
        using var timer = new PeriodicTimer(ComputeInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                sw.Restart();

                var metrics = await ComputeMetricsAsync(stoppingToken);
                _cache.Set(MetricsCacheKey, metrics, TimeSpan.FromSeconds(5));

                sw.Stop();
                _lastComputeMs = sw.Elapsed.TotalMilliseconds;
                _totalComputations++;

                if (_lastComputeMs > _maxComputeMs)
                    _maxComputeMs = _lastComputeMs;

                _avgComputeMs = (_avgComputeMs * (_totalComputations - 1) + _lastComputeMs) / _totalComputations;

                if (_totalComputations % 120 == 0)
                {
                    _logger.LogInformation(
                        "MetricsAggregator stats: {Total} computations, avg {Avg:F2}ms, max {Max:F2}ms, last {Last:F2}ms",
                        _totalComputations, _avgComputeMs, _maxComputeMs, _lastComputeMs);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error computing metrics");
            }
        }

        _logger.LogInformation("MetricsAggregator stopped. Total computations: {Total}, Avg latency: {Avg:F2}ms",
            _totalComputations, _avgComputeMs);
    }

    public async Task<DashboardMetricsDto> ComputeMetricsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var oneMinAgo = now.AddMinutes(-1);
        var fiveMinAgo = now.AddMinutes(-5);
        var oneHourAgo = now.AddHours(-1);

        var transactions = await context.Transactions
            .AsNoTracking()
            .Where(t => t.CreatedAt >= oneHourAgo)
            .ToListAsync(cancellationToken);

        var txns1Min = transactions.Where(t => t.CreatedAt >= oneMinAgo).ToList();
        var txns5Min = transactions.Where(t => t.CreatedAt >= fiveMinAgo).ToList();
        var txns1Hour = transactions;

        var completedTotal = txns1Hour.Count(t => t.Status == TransactionStatus.Completed);
        var failedTotal = txns1Hour.Count(t => t.Status == TransactionStatus.Failed);
        var totalFinished = completedTotal + failedTotal;

        var processedTxns = txns1Hour
            .Where(t => t.ProcessedAt.HasValue && t.CreatedAt > DateTimeOffset.MinValue)
            .ToList();

        var avgProcessingMs = processedTxns.Count > 0
            ? processedTxns.Average(t => (t.ProcessedAt!.Value - t.CreatedAt).TotalMilliseconds)
            : 0;

        var tps = txns1Min.Count > 0
            ? txns1Min.Count / Math.Max(1, (now - txns1Min.Min(t => t.CreatedAt)).TotalSeconds)
            : 0;

        var topSources = txns1Hour
            .GroupBy(t => t.Source)
            .Select(g => new SourceBreakdown(g.Key, g.Count(), g.Sum(t => t.Amount)))
            .OrderByDescending(s => s.Count)
            .Take(5)
            .ToList();

        var flaggedCount = txns1Hour.Count(t => t.Status == TransactionStatus.Flagged);

        return new DashboardMetricsDto
        {
            TotalTransactions1Min = txns1Min.Count,
            TotalTransactions5Min = txns5Min.Count,
            TotalTransactions1Hour = txns1Hour.Count,
            TotalVolume1Min = txns1Min.Sum(t => t.Amount),
            TotalVolume5Min = txns5Min.Sum(t => t.Amount),
            TotalVolume1Hour = txns1Hour.Sum(t => t.Amount),
            SuccessRate = totalFinished > 0 ? Math.Round((double)completedTotal / totalFinished * 100, 1) : 0,
            FailureRate = totalFinished > 0 ? Math.Round((double)failedTotal / totalFinished * 100, 1) : 0,
            AverageProcessingTimeMs = Math.Round(avgProcessingMs, 1),
            TransactionsPerSecond = Math.Round(tps, 1),
            TopSources = topSources,
            FlaggedCount = flaggedCount,
            ActiveConnections = DashboardHub.ConnectionCount,
            ComputedAt = now
        };
    }

    public DashboardMetricsDto? GetCachedMetrics()
    {
        return _cache.Get<DashboardMetricsDto>(MetricsCacheKey);
    }
}
