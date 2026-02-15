# Performance Benchmarks

## Methodology
- Machine: [spec here after testing]
- .NET 8.0, Release mode
- MySQL 8.0 (local Docker)
- Measured with `Stopwatch` + `dotnet-counters`

## Results

| Metric | Target | Measured | Notes |
|--------|--------|----------|-------|
| SignalR broadcast latency | < 50ms | TBD | Hub → all clients |
| Metrics computation | < 100ms | TBD | Aggregate query on 100K rows |
| Batch DB write (100 rows) | < 50ms | TBD | Multi-row INSERT |
| Memory (steady state) | < 512MB | TBD | 50 TPS, 200 connections |
| Memory (peak) | < 1GB | TBD | 100 TPS, 500 connections |
| Chart.js render | < 16ms | TBD | 300 data points, 60fps target |
| Full pipeline (txn → chart) | < 600ms | TBD | End-to-end with 500ms batch |
| Max concurrent connections | 500+ | TBD | Before degradation |
| Max sustained TPS | 100+ | TBD | Without backpressure |

## GC Metrics
| Metric | Value |
|--------|-------|
| Gen 0 collections/min | TBD |
| Gen 1 collections/min | TBD |
| Gen 2 collections/min | TBD |
| Working set (MB) | TBD |

## How to Run Benchmarks
```bash
# Start app in Release mode
cd src/RealTimeDashboard
dotnet run -c Release

# In another terminal, monitor
dotnet-counters monitor --process-id <PID> --counters System.Runtime

# Run load test
cd src/RealTimeDashboard.Tests
dotnet test --filter "Category=LoadTest"
```
