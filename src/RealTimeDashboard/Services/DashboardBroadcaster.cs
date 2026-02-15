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

        // Buffer for collecting transactions between broadcasts
        var buffer = new List<TransactionEntity>();

        using var timer = new PeriodicTimer(BroadcastInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // Drain available transactions from the channel (non-blocking)
                while (_channel.Reader.TryRead(out var transaction))
                {
                    buffer.Add(transaction);
                }

                if (DashboardHub.ConnectionCount == 0)
                {
                    buffer.Clear();
                    continue;
                }

                // Broadcast transaction batch if any
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

                    // Check for flagged transactions and send alerts
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

                    _logger.LogDebug("Broadcast {Count} transactions to {Connections} clients",
                        buffer.Count, DashboardHub.ConnectionCount);

                    buffer.Clear();
                }

                // Always broadcast latest metrics
                var metrics = _metricsAggregator.GetCachedMetrics();
                if (metrics is not null)
                {
                    await _hubContext.Clients.All.ReceiveMetricsUpdate(metrics);
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

        _logger.LogInformation("DashboardBroadcaster stopped");
    }
}
