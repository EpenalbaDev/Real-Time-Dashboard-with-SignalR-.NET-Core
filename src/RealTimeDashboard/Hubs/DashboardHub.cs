using Microsoft.AspNetCore.SignalR;

namespace RealTimeDashboard.Hubs;

public sealed class DashboardHub : Hub<IDashboardClient>
{
    private static int _connectionCount;
    private readonly ILogger<DashboardHub> _logger;

    public DashboardHub(ILogger<DashboardHub> logger)
    {
        _logger = logger;
    }

    public static int ConnectionCount => _connectionCount;

    public override Task OnConnectedAsync()
    {
        var count = Interlocked.Increment(ref _connectionCount);
        _logger.LogInformation("Client connected: {ConnectionId}. Total connections: {Count}",
            Context.ConnectionId, count);
        return base.OnConnectedAsync();
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
}
