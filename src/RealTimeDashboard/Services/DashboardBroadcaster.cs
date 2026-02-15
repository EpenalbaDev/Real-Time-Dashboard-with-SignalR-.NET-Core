using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using RealTimeDashboard.Data.Entities;
using RealTimeDashboard.Hubs;
using RealTimeDashboard.Models;

namespace RealTimeDashboard.Services;

public sealed class DashboardBroadcaster : BackgroundService
{
    private readonly IHubContext<DashboardHub, IDashboardClient> _hubContext;
    private readonly TransactionChannel _channel;
    private readonly MetricsAggregator _metricsAggregator;
    private readonly ILogger<DashboardBroadcaster> _logger;

    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromMilliseconds(500);

    // Performance counters
    private long _totalBroadcasts;
    private long _totalTransactionsBroadcast;
    private double _lastBroadcastMs;
    private double _maxBroadcastMs;
    private double _avgBroadcastMs;

    public long TotalBroadcasts => _totalBroadcasts;
    public long TotalTransactionsBroadcast => _totalTransactionsBroadcast;
    public double LastBroadcastMs => _lastBroadcastMs;
    public double MaxBroadcastMs => _maxBroadcastMs;
    public double AvgBroadcastMs => _avgBroadcastMs;

    public DashboardBroadcaster(
        IHubContext<DashboardHub, IDashboardClient> hubContext,
        TransactionChannel channel,
        MetricsAggregator metricsAggregator,
        ILogger<DashboardBroadcaster> logger)
    {
        _hubContext = hubContext;
        _channel = channel;
        _metricsAggregator = metricsAggregator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DashboardBroadcaster starting (interval: {Interval}ms)",
            BroadcastInterval.TotalMilliseconds);

        var buffer = new List<TransactionEntity>();
        var sw = new Stopwatch();

        using var timer = new PeriodicTimer(BroadcastInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                while (_channel.Reader.TryRead(out var transaction))
                {
                    buffer.Add(transaction);
                }

                if (DashboardHub.ConnectionCount == 0)
                {
                    buffer.Clear();
                    continue;
                }

                sw.Restart();

                if (buffer.Count > 0)
                {
                    var dtos = buffer.Select(t => new TransactionDto(
                        t.Id,
                        t.TransactionId,
                        t.Amount,
                        t.Currency,
                        t.Type,
                        t.Status,
                        t.Source,
                        t.Description,
                        t.CreatedAt,
                        t.ProcessedAt
                    )).ToList();

                    await _hubContext.Clients.All.ReceiveTransactionBatch(dtos);

                    foreach (var flagged in buffer.Where(t => t.Status == TransactionStatus.Flagged))
                    {
                        var alert = new AlertDto(
                            Guid.NewGuid().ToString(),
                            $"Flagged transaction detected: {flagged.TransactionId} (${flagged.Amount:N2})",
                            AlertSeverity.Warning,
                            flagged.TransactionId,
                            DateTimeOffset.UtcNow);

                        await _hubContext.Clients.All.ReceiveAlert(alert);
                    }

                    _totalTransactionsBroadcast += buffer.Count;
                    buffer.Clear();
                }

                var metrics = _metricsAggregator.GetCachedMetrics();
                if (metrics is not null)
                {
                    await _hubContext.Clients.All.ReceiveMetricsUpdate(metrics);
                }

                sw.Stop();
                _lastBroadcastMs = sw.Elapsed.TotalMilliseconds;
                _totalBroadcasts++;

                if (_lastBroadcastMs > _maxBroadcastMs)
                    _maxBroadcastMs = _lastBroadcastMs;

                _avgBroadcastMs = (_avgBroadcastMs * (_totalBroadcasts - 1) + _lastBroadcastMs) / _totalBroadcasts;

                if (_totalBroadcasts % 120 == 0) // Log every ~60 seconds
                {
                    _logger.LogInformation(
                        "Broadcaster stats: {Total} broadcasts, avg {Avg:F2}ms, max {Max:F2}ms, last {Last:F2}ms, {Connections} clients",
                        _totalBroadcasts, _avgBroadcastMs, _maxBroadcastMs, _lastBroadcastMs, DashboardHub.ConnectionCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during broadcast");
            }
        }

        _logger.LogInformation("DashboardBroadcaster stopped. Total broadcasts: {Total}, Avg latency: {Avg:F2}ms",
            _totalBroadcasts, _avgBroadcastMs);
    }
}
