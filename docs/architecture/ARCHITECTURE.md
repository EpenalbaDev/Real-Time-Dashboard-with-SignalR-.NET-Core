# Architecture: Real-Time Dashboard with SignalR + .NET 8

## Problem Statement

Most "real-time dashboard" tutorials demonstrate a chat app pattern: one message in, one broadcast out. This breaks immediately at scale because:

1. **Per-event broadcasting** at 100 TPS = 100 SignalR messages/second to every connected client
2. **Live DB queries** per dashboard refresh = N+1 disaster under load
3. **No backpressure** = memory grows unbounded when clients can't keep up
4. **Chart rendering** at 100fps freezes the browser

This document describes an architecture that handles 100K+ daily transactions with sub-100ms dashboard updates.

## System Overview

```
See diagrams/system-overview.mmd
```

### Core Components

| Component | Responsibility | Pattern |
|-----------|---------------|---------|
| TransactionProcessor | Generate/ingest transactions | BackgroundService + Channel<T> |
| MetricsAggregator | Pre-compute dashboard metrics | BackgroundService + IMemoryCache |
| DashboardBroadcaster | Batch and send SignalR updates | BackgroundService + Timer |
| DashboardHub | Manage WebSocket connections | SignalR Hub<IDashboardClient> |
| Blazor Components | Render charts and data | Chart.js interop + Virtualize |

## Key Architecture Decisions

### 1. Batched Broadcasting (500ms Interval)

**Problem:** At 100 TPS, per-transaction broadcasting means:
- 100 serializations/second
- 100 × N network writes (N = connected clients)
- 100 Chart.js re-renders on each client

**Solution:** Collect transactions in a buffer, broadcast batch every 500ms.
- Max 2 broadcasts/second regardless of TPS
- Single serialization per batch
- Client receives array, updates chart once

**Why 500ms?** Human perception threshold for "real-time" on dashboards is ~200-500ms. Below 200ms, users can't distinguish individual updates. Above 1000ms, it feels laggy. 500ms is the sweet spot.

### 2. Pre-Computed Metrics Pipeline

**Problem:** Computing `SUM(amount) WHERE createdAt > NOW() - INTERVAL 5 MINUTE` on every dashboard refresh at 100 TPS:
- Full table scans grow linearly with data
- 500 connected clients × 2 refreshes/second = 1000 queries/second

**Solution:** MetricsAggregator runs as background service:
- Computes metrics every 500ms
- Stores in IMemoryCache (key: metric name + period)
- Broadcaster reads cache, never touches DB directly
- DB reads happen once per cycle, not per client

### 3. Channel<T> as Internal Message Bus

**Why not ConcurrentQueue?**
- Channel<T> supports async enumeration (await foreach)
- Built-in backpressure: `BoundedChannel` blocks producer when full
- Clean cancellation via CancellationToken
- Multiple consumers possible (DB writer + broadcaster read from same channel via fanout)

```csharp
var channel = Channel.CreateBounded<Transaction>(new BoundedChannelOptions(10_000)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = false,
    SingleWriter = false
});
```

### 4. Database Write Strategy

**Problem:** Writing each transaction individually = 100 INSERT/second = unnecessary DB pressure.

**Solution:** Batch writer collects from Channel, flushes every:
- 100 transactions accumulated, OR
- 1 second elapsed (whichever comes first)

Uses `ExecuteSqlRawAsync` with multi-row INSERT for 10x throughput vs individual EF SaveChanges.

## Scaling Paths (Not Implemented — Discussed for Blog)

### At 500K Daily Transactions (~6 TPS average, 50 TPS peak)
- Current architecture handles this without changes
- Consider: MySQL partitioning by date

### At 1M+ Daily Transactions (~12 TPS average, 100+ TPS peak)
- **Add Redis** for metrics cache (share across instances)
- **SignalR Redis Backplane** for horizontal scaling
- **Read replica** for MetricsAggregator queries

### At 10M+ Daily Transactions
- **CQRS full split:** Write to MySQL, read from materialized views or ClickHouse
- **Kafka/RabbitMQ** replaces Channel<T> for cross-service communication
- **SignalR on Azure SignalR Service** (managed, auto-scaling)

## Performance Characteristics

See `benchmarks/BENCHMARKS.md` for measured numbers.

Expected targets:
- Dashboard refresh: < 100ms (cache read + serialize + broadcast)
- Memory steady state: < 512MB
- Concurrent connections: 500+ on single instance
- Transaction throughput: 100+ TPS simulated

## Trade-offs & Limitations

1. **No authentication** — production would add JWT/cookie auth on the hub
2. **In-memory cache** — single instance only; Redis needed for multi-instance
3. **Simulated data** — real production would ingest from message queue
4. **No persistence for metrics** — recomputed on restart; production would persist snapshots
5. **MySQL for time-series** — works fine at this scale; TimescaleDB/ClickHouse at 10M+
