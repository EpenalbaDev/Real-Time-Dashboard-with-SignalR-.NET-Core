using Microsoft.AspNetCore.SignalR;
using RealTimeDashboard.Services;

namespace RealTimeDashboard.Hubs;

public sealed class DashboardHub : Hub<IDashboardClient>
{
    private static int _connectionCount;
    private readonly TransactionProcessorService _processor;
    private readonly ILogger<DashboardHub> _logger;

    public DashboardHub(TransactionProcessorService processor, ILogger<DashboardHub> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    public static int ConnectionCount => _connectionCount;

    public override async Task OnConnectedAsync()
    {
        var count = Interlocked.Increment(ref _connectionCount);
        _logger.LogInformation("Client connected: {ConnectionId}. Total connections: {Count}",
            Context.ConnectionId, count);

        // Send current demo status to the new client
        await Clients.Caller.ReceiveDemoStatus(_processor.IsDemoRunning, _processor.RemainingSeconds);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var count = Interlocked.Decrement(ref _connectionCount);
        _logger.LogInformation("Client disconnected: {ConnectionId}. Total connections: {Count}",
            Context.ConnectionId, count);

        if (exception is not null)
        {
            _logger.LogWarning(exception, "Client {ConnectionId} disconnected with error",
                Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    public async Task StartDemo()
    {
        var started = _processor.StartDemo();
        if (started)
        {
            _logger.LogInformation("Demo started by client {ConnectionId}", Context.ConnectionId);
            await Clients.All.ReceiveDemoStatus(true, _processor.RemainingSeconds);
        }
        else
        {
            // Already running, send current status
            await Clients.Caller.ReceiveDemoStatus(true, _processor.RemainingSeconds);
        }
    }
}
