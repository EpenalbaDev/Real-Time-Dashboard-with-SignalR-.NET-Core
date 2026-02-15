# Architecture: Real-Time Dashboard with SignalR + .NET 8

## Problem Statement

Most "real-time dashboard" tutorials demonstrate a chat app pattern: one message in, one broadcast out. This breaks immediately at scale because:

1. **Per-event broadcasting** at 100 TPS = 100 SignalR messages/second to every connected client
2. **Live DB queries** per dashboard refresh = N+1 disaster under load
3. **No backpressure** = memory grows unbounded when clients can't keep up
4. **Chart rendering** at 100fps freezes the browser

This document describes an architecture that handles 100K+ daily transactions with sub-100ms dashboard updates on a single server instance.

## System Overview

![System Overview](diagrams/system-overview.mmd)

The application is a Blazor Server app with three BackgroundServices forming a data pipeline:

```
TransactionProcessor → Channel<T> → DashboardBroadcaster → SignalR Hub → Clients
                    ↘ MySQL (batch)   ↗ IMemoryCache
               MetricsAggregator → MySQL (read)
```

### Core Components

| Component | Type | Responsibility | Interval |
|-----------|------|---------------|----------|
| `TransactionProcessorService` | BackgroundService | Generate simulated transactions, write to DB in batches | Configurable TPS (default: 10) |
| `MetricsAggregator` | BackgroundService | Pre-compute dashboard metrics from DB | Every 500ms |
| `DashboardBroadcaster` | BackgroundService | Batch and send SignalR updates to all clients | Every 500ms |
| `DashboardHub` | SignalR Hub<IDashboardClient> | Manage WebSocket connections, track connection count | On connect/disconnect |
| Dashboard.razor | Blazor Component | SignalR client with auto-reconnect, distributes data to child components | On receive |

### Technology Stack

| Layer | Technology | Why |
|-------|-----------|-----|
| Frontend | Blazor Server + Chart.js | Server-side rendering, real-time via SignalR circuit |
| Real-time | SignalR (built into ASP.NET Core) | WebSocket transport, typed hub, automatic reconnect |
| Charts | Chart.js 4.x via JS Interop | Lightweight, performant canvas rendering |
| Database | MySQL 8.0 via EF Core + Pomelo | Relational storage with good .NET support |
| Caching | IMemoryCache | In-process, zero-config, fast reads |
| Message Bus | Channel<T> (System.Threading.Channels) | In-process async producer-consumer with backpressure |
| Testing | xUnit + Moq + EF Core InMemory | Standard .NET testing stack |

## Key Architecture Decisions

### 1. Batched Broadcasting (500ms Interval)

**Problem:** At 100 TPS, per-transaction broadcasting means:
- 100 JSON serializations/second
- 100 x N network writes (N = connected clients)
- 100 Chart.js re-renders on each client

**Solution:** Collect transactions in a buffer, broadcast batch every 500ms.
- Max 2 broadcasts/second regardless of TPS
- Single serialization per batch
- Client receives array, updates chart once

**Result:** 50x reduction in broadcasts, serializations, and network I/O.

**Why 500ms?** Human perception threshold for "real-time" on dashboards is ~200-500ms. Below 200ms, users can't distinguish individual updates. Above 1000ms, it feels laggy. 500ms is the sweet spot — fast enough to feel live, slow enough to batch efficiently.

```csharp
// DashboardBroadcaster.cs - core loop
while (await timer.WaitForNextTickAsync(stoppingToken))
{
    // Drain all available transactions from channel
    while (_channel.Reader.TryRead(out var transaction))
        buffer.Add(transaction);

    if (buffer.Count > 0)
    {
        await _hubContext.Clients.All.ReceiveTransactionBatch(dtos);
        buffer.Clear();
    }

    var metrics = _metricsAggregator.GetCachedMetrics();
    if (metrics is not null)
        await _hubContext.Clients.All.ReceiveMetricsUpdate(metrics);
}
```

### 2. Pre-Computed Metrics Pipeline

**Problem:** Computing `SUM(amount) WHERE createdAt > NOW() - INTERVAL 5 MINUTE` on every dashboard refresh:
- Full table scans grow linearly with data
- N connected clients x 2 refreshes/second = 2N queries/second

**Solution:** MetricsAggregator runs as a BackgroundService:
- Queries the DB once every 500ms (single reader, never per-client)
- Computes all aggregations: TPS, success/failure rates, volumes across 1min/5min/1hr windows, top sources, flagged count
- Stores result in IMemoryCache with 5-second TTL
- DashboardBroadcaster reads from cache (O(1)), never touches DB directly

**Result:** 1 DB query per 500ms instead of 2N queries per second. With 200 clients, this is a 200x reduction in DB load.

### 3. Channel<T> as Internal Message Bus

**Why not ConcurrentQueue?**
- Channel<T> supports `async WriteAsync` (awaitable producer)
- Built-in backpressure: `BoundedChannel` with `FullMode.Wait` blocks the producer when the buffer is full
- Clean cancellation via CancellationToken
- Single reader optimization available

```csharp
// TransactionChannel.cs
Channel.CreateBounded<TransactionEntity>(new BoundedChannelOptions(10_000)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = true,   // Only DashboardBroadcaster reads
    SingleWriter = false   // TransactionProcessor writes
});
```

**Why not one Channel for both DB writes and broadcasting?**
Initial design had both the DB writer and broadcaster reading from the same channel. Problem: `Channel.Reader.ReadAsync()` consumes items — one reader gets the item, the other doesn't. Solution: TransactionProcessor maintains a separate internal `List<T>` with a lock for DB batch writes, and uses the Channel exclusively for the broadcaster.

### 4. Database Write Strategy

**Problem:** Writing each transaction individually = N INSERTs/second = unnecessary DB pressure.

**Solution:** TransactionProcessor accumulates transactions in an internal batch and flushes every 1 second using EF Core's `AddRange` + `SaveChangesAsync` (which generates a multi-row INSERT).

```csharp
// TransactionProcessorService.cs
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

### 5. Typed SignalR Hub

Using `Hub<IDashboardClient>` instead of untyped `Hub`:
- Compile-time safety for all client method calls
- No magic strings for method names
- Easier refactoring and IntelliSense support

```csharp
public interface IDashboardClient
{
    Task ReceiveTransactionBatch(List<TransactionDto> transactions);
    Task ReceiveMetricsUpdate(DashboardMetricsDto metrics);
    Task ReceiveAlert(AlertDto alert);
}

public class DashboardHub : Hub<IDashboardClient> { }
```

### 6. Client-Side Reconnection Strategy

The Blazor client uses exponential backoff for SignalR reconnection:

```csharp
.WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2),
    TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
```

The `StatusIndicator` component shows connection state (Connected/Reconnecting/Disconnected) with visual feedback.

### 7. Chart.js Integration via JS Interop

Charts are rendered via JavaScript interop rather than a .NET chart library:
- Chart.js is battle-tested for real-time data visualization
- Canvas rendering is GPU-accelerated
- Sliding window pattern (300 data points) prevents unbounded memory growth
- Dark theme colors configured via chart options

```csharp
// ChartJsInterop.cs wraps IJSRuntime
await _jsRuntime.InvokeVoidAsync("chartInterop.updateChartData",
    chartId, labels, data);
```

## Data Flow (Detailed)

![Data Pipeline](diagrams/data-pipeline.mmd)

1. **TransactionProcessor** generates a transaction at configurable TPS
2. Transaction is added to both the internal DB batch and the Channel<T>
3. **DB batch** flushes to MySQL every 1 second via `SaveChangesAsync`
4. **MetricsAggregator** reads from MySQL every 500ms, computes metrics, writes to IMemoryCache
5. **DashboardBroadcaster** drains the Channel every 500ms, reads cached metrics
6. Broadcaster sends `ReceiveTransactionBatch` + `ReceiveMetricsUpdate` + optional `ReceiveAlert` via SignalR Hub
7. All connected Blazor clients receive the batch, update charts/tables/cards in a single render cycle

## Performance Instrumentation

All three BackgroundServices include `Stopwatch`-based performance counters:

| Service | Metrics Tracked |
|---------|----------------|
| DashboardBroadcaster | TotalBroadcasts, AvgBroadcastMs, MaxBroadcastMs, TotalTransactionsBroadcast |
| MetricsAggregator | TotalComputations, AvgComputeMs, MaxComputeMs |
| TransactionProcessorService | TotalTransactionsProduced, TotalDbFlushes, TotalDbRowsWritten, AvgFlushMs, MaxFlushMs |

All counters are exposed via `GET /api/diagnostics` for live monitoring.

See `benchmarks/BENCHMARKS.md` for measured performance numbers.

## Scaling Paths

![Scaling Strategy](diagrams/scaling-strategy.mmd)

### Current: Single Instance (100K/day, ~1.2 TPS average)
- Everything runs in-process
- IMemoryCache for metrics
- MySQL single instance
- Channel<T> for in-process messaging

### At 500K/day (~6 TPS average, 50 TPS peak)
Current architecture handles this without changes. Consider:
- MySQL date partitioning for query performance
- Connection pooling tuning

### At 1M+/day (~12 TPS average, 100+ TPS peak)
- **Redis** replaces IMemoryCache (shared across instances)
- **SignalR Redis Backplane** for horizontal scaling of WebSocket connections
- **MySQL read replica** for MetricsAggregator queries (separate read/write)
- Load balancer with sticky sessions for WebSocket affinity

### At 10M+/day (Full CQRS)
- **Kafka/RabbitMQ** replaces Channel<T> for cross-service communication
- **Azure SignalR Service** (managed, auto-scaling WebSocket infrastructure)
- **ClickHouse/TimescaleDB** for time-series reads (MetricsAggregator)
- Separate write service and read service

## Trade-offs & Limitations

1. **No authentication** — production would add JWT/cookie auth on the hub
2. **In-memory cache** — single instance only; Redis needed for multi-instance
3. **Simulated data** — real production would ingest from message queue or API
4. **No persistence for metrics** — recomputed on restart; production would persist snapshots
5. **MySQL for time-series** — works fine at this scale; TimescaleDB/ClickHouse at 10M+
6. **JSON serialization** — MessagePack would reduce payload size ~30% but adds complexity
7. **No rate limiting on hub** — production would throttle connections per IP
8. **EF Core for batch writes** — raw SQL or BulkExtensions would be faster at high TPS

## Lessons Learned

1. **Batching is the single biggest optimization.** Going from per-event to 500ms batching gives a 50x reduction in all downstream work.
2. **Channel<T> consumers are exclusive.** One reader consumes the item — you can't have two readers both see every item without fan-out logic.
3. **PeriodicTimer is better than Task.Delay.** No drift accumulation over time, cleaner cancellation.
4. **Pre-computing is always worth it** when read frequency exceeds write frequency (N clients >> 1 aggregator).
5. **IMemoryCache is underrated.** Zero-config, zero-latency reads for single-instance scenarios.
6. **Typed hubs catch bugs at compile time.** No more `Clients.All.SendAsync("RecieveData", ...)` typo disasters.

## Project Structure

```
src/
├── RealTimeDashboard/
│   ├── Data/
│   │   ├── Entities/          # TransactionEntity, enums
│   │   ├── AppDbContext.cs    # EF Core context + Fluent API
│   │   └── DataSeeder.cs     # 10K seed transactions
│   ├── Hubs/
│   │   ├── DashboardHub.cs   # SignalR hub + connection tracking
│   │   └── IDashboardClient.cs # Typed client interface
│   ├── Models/
│   │   ├── TransactionDto.cs  # DTOs + PagedResult<T>
│   │   ├── DashboardMetricsDto.cs
│   │   ├── AlertDto.cs
│   │   └── DiagnosticsDto.cs  # Performance counters
│   ├── Services/
│   │   ├── TransactionProcessorService.cs  # Producer + DB writer
│   │   ├── TransactionChannel.cs           # Channel<T> wrapper
│   │   ├── MetricsAggregator.cs            # Pre-computation
│   │   ├── DashboardBroadcaster.cs         # Batched SignalR
│   │   ├── TransactionService.cs           # CRUD service
│   │   └── ChartJsInterop.cs              # Chart.js wrapper
│   ├── Pages/
│   │   ├── Dashboard.razor    # Main dashboard + SignalR client
│   │   ├── Transactions.razor # Paginated transaction list
│   │   └── Index.razor        # Redirect to dashboard
│   ├── Shared/
│   │   ├── Components/        # MetricsCard, StatusIndicator, charts, table
│   │   ├── MainLayout.razor
│   │   └── NavMenu.razor
│   ├── wwwroot/
│   │   ├── css/site.css       # Dark theme
│   │   └── js/chartInterop.js # Chart.js interop
│   └── Program.cs             # DI, middleware, endpoints
├── RealTimeDashboard.Tests/
│   ├── Services/              # Unit tests (TransactionService, MetricsAggregator)
│   └── LoadTests/             # SignalR load tests (50 concurrent connections)
docs/
├── architecture/
│   ├── ARCHITECTURE.md        # This file
│   ├── benchmarks/BENCHMARKS.md
│   └── diagrams/
│       ├── system-overview.mmd
│       ├── signalr-flow.mmd
│       ├── data-pipeline.mmd
│       └── scaling-strategy.mmd
└── phases/                    # Development phase specifications
```
