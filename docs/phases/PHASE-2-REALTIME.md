# Phase 2: Real-Time Pipeline

## Objective
Implement SignalR hub, background transaction processor, metrics aggregator, and batched broadcasting.

## Tasks

### 2.1 Define Typed Hub Interface
```csharp
public interface IDashboardClient
{
    Task ReceiveTransactionBatch(IReadOnlyList<TransactionDto> transactions);
    Task ReceiveMetricsUpdate(DashboardMetricsDto metrics);
    Task ReceiveAlert(AlertDto alert);
}
```

### 2.2 Create DashboardHub
```csharp
[Authorize] // TODO: Phase future — for now, no auth
public sealed class DashboardHub : Hub<IDashboardClient>
{
    public override async Task OnConnectedAsync() { /* log connection, track count */ }
    public override async Task OnDisconnectedAsync(Exception? ex) { /* cleanup */ }
}
```

### 2.3 TransactionProcessorService (BackgroundService)
This is the **core of the demo**. It simulates a production transaction flow:

- Runs as `IHostedService`
- Generates transactions at configurable TPS (default: 10 TPS, scalable to 100+)
- Each transaction goes through: `Pending → Processing → Completed/Failed`
- Processing delay: random 100ms-2000ms to simulate real bank processing
- Uses `Channel<Transaction>` as internal queue (producer-consumer pattern)
- Writes to DB in batches (every 100 transactions or 1 second, whichever first)

### 2.4 MetricsAggregator Service
Pre-computes dashboard metrics to avoid N+1 queries:

```csharp
public sealed class MetricsAggregator : BackgroundService
{
    // Every 500ms, compute:
    // - Total transactions (last 1min, 5min, 1hour)
    // - Total volume ($) by period
    // - Success/failure rate
    // - Average processing time
    // - Transactions per second (current)
    // - Top transaction sources
    // - Flagged transaction count
}
```

### 2.5 Batched SignalR Broadcasting
**Critical for scale.** Never broadcast per-transaction.

```csharp
public sealed class DashboardBroadcaster : BackgroundService
{
    private readonly IHubContext<DashboardHub, IDashboardClient> _hub;
    
    // Every 500ms:
    // 1. Collect new transactions from Channel<T>
    // 2. Get latest metrics from MetricsAggregator
    // 3. Single broadcast to all connected clients
    // 4. If flagged transaction detected → send alert
}
```

### 2.6 Configure SignalR in Program.cs
```csharp
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 64 * 1024; // 64KB
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});
```

## Key Architecture Decisions (document in ARCHITECTURE.md)
1. **Why Channel<T> over ConcurrentQueue:** Backpressure support, async enumeration
2. **Why batch at 500ms:** Human eye can't perceive faster updates on charts; reduces SignalR overhead by 99%
3. **Why pre-compute metrics:** At 100 TPS, querying raw data per refresh = death
4. **Why single hub:** Multiple hubs add complexity without benefit at this scale

## Definition of Done
- [ ] Transactions generate continuously when app starts
- [ ] SignalR hub accepts connections
- [ ] Console/logs show batched broadcasts every 500ms
- [ ] Metrics update in real-time
- [ ] No memory leaks after 10 minutes of running (check with `dotnet-counters`)
- [ ] Unit tests for MetricsAggregator calculations
- [ ] Git: commit on `phase/2-realtime`, tag `v0.2.0`

## Estimated Time: 3-4 hours with Claude Code
