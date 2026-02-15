# Building a Real-Time Dashboard That Actually Scales: SignalR + .NET 8

Most SignalR tutorials show you how to build a chat app. Here's how to build something that actually handles production load — a real-time financial transaction dashboard processing 100K+ transactions per day.

## The Problem: Why Naive Real-Time Dashboards Break

Every "real-time dashboard" tutorial I've seen follows the same pattern:

1. Transaction comes in
2. Broadcast it to all connected clients
3. Each client queries the database for updated metrics
4. Re-render the chart

This works beautifully with 5 users and 1 transaction per second. It falls apart at scale because:

**Per-event broadcasting at 100 TPS** means 100 SignalR messages per second to every connected client. With 200 clients, that's 20,000 WebSocket writes per second — just for broadcasting.

**Live DB queries per refresh** means every client, on every update, runs `SELECT SUM(amount) WHERE created_at > NOW() - INTERVAL 5 MINUTE`. With 200 clients refreshing twice per second, that's 400 aggregate queries per second hitting your database.

**No backpressure** means if clients can't keep up (slow network, heavy chart rendering), messages queue up in memory until your server runs out of RAM.

**Chart rendering at 100fps** freezes the browser. Chart.js is fast, but not "re-render a line chart 100 times per second" fast.

I built a dashboard that solves all four problems. Here's how.

## Architecture Overview

The application is a Blazor Server app with three BackgroundServices forming a data pipeline:

```
TransactionProcessor → Channel<T> → DashboardBroadcaster → SignalR Hub → Clients
                    ↘ MySQL (batch)   ↗ IMemoryCache
               MetricsAggregator → MySQL (read)
```

| Component | What It Does |
|-----------|-------------|
| **TransactionProcessor** | Generates simulated transactions at configurable TPS, batch-writes to MySQL every 1 second |
| **MetricsAggregator** | Queries MySQL every 500ms, pre-computes all dashboard metrics, stores in IMemoryCache |
| **DashboardBroadcaster** | Drains the Channel every 500ms, reads cached metrics, broadcasts everything to all clients in one batch |

The key insight: **separate the concerns of data ingestion, metric computation, and client delivery into independent loops running at their own cadences.**

## The Three Key Patterns

### Pattern 1: Batched Broadcasting

Instead of broadcasting every transaction as it happens, we collect transactions in a buffer and send them all at once every 500ms.

```csharp
// DashboardBroadcaster.cs
while (await timer.WaitForNextTickAsync(stoppingToken))
{
    // Drain all available transactions from channel
    while (_channel.Reader.TryRead(out var transaction))
        buffer.Add(transaction);

    if (buffer.Count > 0)
    {
        var dtos = buffer.Select(t => new TransactionDto(/* ... */)).ToList();
        await _hubContext.Clients.All.ReceiveTransactionBatch(dtos);
        buffer.Clear();
    }

    var metrics = _metricsAggregator.GetCachedMetrics();
    if (metrics is not null)
        await _hubContext.Clients.All.ReceiveMetricsUpdate(metrics);
}
```

**The impact is massive:**

| Scenario | Broadcasts/sec | Serializations/sec | Network writes/sec (N clients) |
|----------|---------------|--------------------|---------------------------------|
| Naive (per-txn at 100 TPS) | 100 | 100 | 100 x N |
| **Batched (500ms)** | **2** | **2** | **2 x N** |
| **Improvement** | **50x** | **50x** | **50x** |

**Why 500ms?** Human perception threshold for "real-time" on dashboards is ~200-500ms. Below 200ms, users can't distinguish individual updates. Above 1000ms, it feels laggy. 500ms is the sweet spot.

On the client side, Chart.js receives an array and does a single render update, instead of 100 separate re-renders.

### Pattern 2: Pre-Computed Metrics

Instead of having each client query the database for dashboard metrics, a single BackgroundService computes everything once and caches it.

```csharp
// MetricsAggregator.cs
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        var metrics = await ComputeMetricsAsync(stoppingToken);
        _cache.Set(MetricsCacheKey, metrics, TimeSpan.FromSeconds(5));
    }
}
```

The `ComputeMetricsAsync` method queries the last hour of transactions once and computes everything: transaction counts across 1min/5min/1hr windows, volume sums, TPS, success/failure rates, top sources, flagged count, and average processing time.

The DashboardBroadcaster reads from the cache (an O(1) dictionary lookup), never from the database:

```csharp
var metrics = _metricsAggregator.GetCachedMetrics(); // IMemoryCache read, not DB query
```

**With 200 connected clients, this is a 200x reduction in database load.** One query per 500ms instead of 400 queries per second.

### Pattern 3: Channel\<T\> as Internal Message Bus

`System.Threading.Channels` provides an async producer-consumer queue with built-in backpressure:

```csharp
// TransactionChannel.cs
Channel.CreateBounded<TransactionEntity>(new BoundedChannelOptions(10_000)
{
    FullMode = BoundedChannelFullMode.Wait,  // Block producer when full
    SingleReader = true,   // Only DashboardBroadcaster reads
    SingleWriter = false
});
```

Why Channel\<T\> instead of `ConcurrentQueue`?
- **Async enumeration** — `WriteAsync` and `TryRead` are awaitable
- **Backpressure** — `BoundedChannel` with `FullMode.Wait` blocks the producer when the buffer is full (10K items), preventing unbounded memory growth
- **Clean cancellation** via CancellationToken
- **Single-reader optimization** when only one consumer needs the data

**A lesson learned:** Initially, I had both the database writer and the broadcaster reading from the same channel. Problem: `Channel.Reader.ReadAsync()` _consumes_ items — one reader gets the item, the other doesn't. Solution: the TransactionProcessor maintains a separate internal `List<T>` with a lock for database batch writes, and uses the Channel exclusively for the broadcaster.

## Implementation Deep-Dive

### Typed SignalR Hub

Using `Hub<IDashboardClient>` instead of the untyped `Hub` gives compile-time safety:

```csharp
public interface IDashboardClient
{
    Task ReceiveTransactionBatch(List<TransactionDto> transactions);
    Task ReceiveMetricsUpdate(DashboardMetricsDto metrics);
    Task ReceiveAlert(AlertDto alert);
}

public class DashboardHub : Hub<IDashboardClient>
{
    private static int _connectionCount;
    public static int ConnectionCount => _connectionCount;

    public override Task OnConnectedAsync()
    {
        Interlocked.Increment(ref _connectionCount);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Interlocked.Decrement(ref _connectionCount);
        return base.OnDisconnectedAsync(exception);
    }
}
```

No more `Clients.All.SendAsync("RecieveData", ...)` typo disasters that only fail at runtime.

### Database Write Strategy

Writing each transaction individually means N INSERTs per second. Instead, the TransactionProcessor accumulates transactions and flushes every 1 second:

```csharp
private async Task FlushBatchAsync()
{
    List<TransactionEntity> toFlush;
    lock (_batchLock)
    {
        if (_dbBatch.Count == 0) return;
        toFlush = new List<TransactionEntity>(_dbBatch);
        _dbBatch.Clear();
    }

    context.Transactions.AddRange(toFlush);
    await context.SaveChangesAsync();  // Multi-row INSERT
}
```

EF Core's `AddRange` + `SaveChangesAsync` generates a multi-row INSERT statement, which is significantly faster than individual inserts.

### Client-Side: Blazor + Chart.js

The dashboard uses Blazor Server with Chart.js via JavaScript interop. The Blazor client connects to the SignalR hub with automatic reconnection:

```csharp
_hubConnection = new HubConnectionBuilder()
    .WithUrl(Navigation.ToAbsoluteUri("/hubs/dashboard"))
    .WithAutomaticReconnect(new[] {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    })
    .Build();
```

Charts use a sliding window of 300 data points to prevent unbounded memory growth. When a new batch arrives, older points are shifted out, and the chart updates once.

### Performance Instrumentation

All three BackgroundServices include `Stopwatch`-based performance counters, exposed via a diagnostics endpoint:

```bash
curl http://localhost:8080/api/diagnostics | jq .
```

Returns broadcast latency, metrics computation time, DB flush time, and connection count — all updated in real-time.

## Performance Numbers

Measured with Stopwatch instrumentation in Release mode:

| Metric | Value | Notes |
|--------|-------|-------|
| SignalR broadcast latency | ~2-5ms | JSON serialization + WebSocket write |
| Metrics computation | ~15-30ms | Aggregate query on last 1hr |
| Batch DB write (10 rows) | ~8-15ms | EF Core SaveChangesAsync |
| Chart.js render | ~5-10ms | 300 data points, single update |
| Full pipeline (txn → chart) | ~500-550ms | Dominated by 500ms batch interval |
| Memory (steady state) | ~180-250 MB | 10 TPS, 10 connections |
| Max concurrent connections | 500+ | On single instance |

The full pipeline latency is ~500-550ms — almost entirely the 500ms batch interval. The actual processing adds less than 50ms. If you need lower latency, reduce the batch interval (at the cost of more broadcasts per second).

## Scaling Beyond: What Changes at 500K, 1M, 10M

### At 500K/day (~6 TPS average, 50 TPS peak)

The current architecture handles this without code changes. You might want:
- **MySQL date partitioning** for query performance on the Transactions table
- **Connection pooling tuning** for the batch writer

### At 1M+/day (~12 TPS average, 100+ TPS peak)

Now you need horizontal scaling:
- **Redis** replaces `IMemoryCache` (shared across instances)
- **SignalR Redis Backplane** allows multiple server instances to broadcast to all clients
- **MySQL read replica** for MetricsAggregator queries (separate read/write paths)
- **Load balancer** with sticky sessions for WebSocket affinity

### At 10M+/day (Full CQRS)

Time for a real architecture overhaul:
- **Kafka or RabbitMQ** replaces `Channel<T>` for cross-service communication
- **Azure SignalR Service** (managed, auto-scaling WebSocket infrastructure)
- **ClickHouse or TimescaleDB** for time-series reads (MetricsAggregator)
- Separate write service and read service (true CQRS)

The beauty of the current architecture is that each scaling step is incremental. You don't need to rewrite the application — you replace one component at a time.

## Lessons Learned

1. **Batching is the single biggest optimization.** Going from per-event to 500ms batching gives a 50x reduction in all downstream work. Always ask: "Can I batch this?"

2. **Channel\<T\> consumers are exclusive.** Items are consumed, not peeked. If you need multiple consumers, you need fan-out logic or separate data paths.

3. **PeriodicTimer is better than Task.Delay.** No drift accumulation, cleaner cancellation, and more idiomatic .NET 8.

4. **Pre-computing is always worth it** when read frequency exceeds write frequency. With N clients reading and 1 service writing, pre-computation is an N:1 improvement.

5. **IMemoryCache is underrated.** Zero-config, zero-latency reads, perfect for single-instance scenarios. Don't reach for Redis until you actually need multi-instance.

6. **Typed hubs catch bugs at compile time.** The few minutes setting up `IDashboardClient` save hours of debugging runtime `SendAsync` typos.

## Try It Yourself

The full source code is on GitHub with a `docker-compose.yml` for one-command local setup:

```bash
git clone https://github.com/your-username/Real-Time-Dashboard-with-SignalR-.NET-Core.git
cd Real-Time-Dashboard-with-SignalR-.NET-Core
docker-compose up
# Open http://localhost:8080
```

The architecture documentation, Mermaid diagrams, and benchmark results are all in the `docs/` directory.

---

*Built with .NET 8, Blazor Server, SignalR, MySQL 8.0, and Chart.js. Licensed under MIT.*
