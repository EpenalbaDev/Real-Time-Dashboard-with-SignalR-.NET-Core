# Real-Time Transaction Dashboard

> SignalR + .NET 8 + Blazor Server + MySQL

A production-grade real-time financial transaction dashboard that handles **100K+ daily transactions** with sub-100ms dashboard updates. Built to demonstrate how to scale SignalR beyond chat apps.

## Architecture

```
TransactionProcessor --> Channel<T> --> DashboardBroadcaster --> SignalR Hub --> Clients
                     \-> MySQL (batch)   /-> IMemoryCache
                MetricsAggregator --> MySQL (read)
```

**Three BackgroundServices** form a data pipeline:

| Component | Responsibility | Interval |
|-----------|---------------|----------|
| `TransactionProcessorService` | Generate transactions, batch write to DB | Configurable TPS |
| `MetricsAggregator` | Pre-compute dashboard metrics from DB | Every 500ms |
| `DashboardBroadcaster` | Batch and send SignalR updates to all clients | Every 500ms |

See [`docs/architecture/ARCHITECTURE.md`](docs/architecture/ARCHITECTURE.md) for the full architecture deep-dive.

## Key Features

- **Real-time transaction streaming** via SignalR WebSockets
- **Batched broadcasting** (500ms intervals) - 50x reduction vs per-event
- **Pre-computed metrics** - no live DB queries per client refresh
- **Channel\<T\> backpressure** - bounded 10K buffer prevents OOM
- **Dark theme dashboard** with Chart.js line, doughnut, and bar charts
- **Auto-reconnect** with exponential backoff and visual status indicator
- **Performance diagnostics** endpoint (`/api/diagnostics`)
- **Health check** endpoint (`/health`) with DB connectivity check

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Blazor Server + Chart.js 4.x |
| Real-time | SignalR (WebSocket transport) |
| Database | MySQL 8.0 (EF Core + Pomelo) |
| Caching | IMemoryCache |
| Message Bus | Channel\<T\> (System.Threading.Channels) |
| Testing | xUnit + Moq + EF Core InMemory |
| Deploy | Docker + Railway |

## Quick Start

### Docker Compose (recommended)

```bash
git clone https://github.com/your-username/Real-Time-Dashboard-with-SignalR-.NET-Core.git
cd Real-Time-Dashboard-with-SignalR-.NET-Core
docker-compose up
```

Open [http://localhost:8080](http://localhost:8080) to see the dashboard.

### Local Development

**Prerequisites:** .NET 8 SDK, MySQL 8.0

```bash
# 1. Start MySQL (or use Docker)
docker run -d --name mysql -e MYSQL_ROOT_PASSWORD=dev123 -e MYSQL_DATABASE=dashboard_db -p 3306:3306 mysql:8.0

# 2. Run the app
cd src/RealTimeDashboard
dotnet run

# 3. Open browser
# https://localhost:5001/dashboard
```

### Run Tests

```bash
# Unit tests (23 tests)
dotnet test --filter "Category!=LoadTest"

# Load tests (requires running app)
dotnet test --filter "Category=LoadTest"
```

## Performance

| Metric | Value | Notes |
|--------|-------|-------|
| SignalR broadcast latency | ~2-5ms | Hub to all clients |
| Metrics computation | ~15-30ms | Aggregate query on last 1hr |
| Batch DB write | ~8-15ms | EF Core SaveChangesAsync |
| Full pipeline (txn to chart) | ~500-550ms | End-to-end with 500ms batch |
| Max concurrent connections | 500+ | Single instance |
| Max sustained TPS | 100+ | Without backpressure |

### Broadcast Efficiency

| Scenario | Broadcasts/sec | Network writes/sec (N clients) |
|----------|---------------|-------------------------------|
| Naive (per-txn at 100 TPS) | 100 | 100 x N |
| **Batched (500ms)** | **2** | **2 x N** |
| **Improvement** | **50x fewer** | **50x fewer** |

See [`docs/architecture/benchmarks/BENCHMARKS.md`](docs/architecture/benchmarks/BENCHMARKS.md) for detailed numbers.

## Architecture Diagrams

The project includes 4 Mermaid diagrams in `docs/architecture/diagrams/`:

- **system-overview.mmd** - Component architecture and data flow
- **signalr-flow.mmd** - Sequence diagram of the real-time pipeline
- **data-pipeline.mmd** - Detailed transaction-to-chart flow
- **scaling-strategy.mmd** - Scaling tiers from 100K to 10M+/day

## Project Structure

```
src/
├── RealTimeDashboard/              # Blazor Server app
│   ├── Data/                       # EF Core context, entities, seeder
│   ├── Hubs/                       # SignalR hub + typed client interface
│   ├── Models/                     # DTOs (TransactionDto, MetricsDto, etc.)
│   ├── Services/                   # BackgroundServices + business logic
│   ├── Pages/                      # Dashboard.razor, Transactions.razor
│   ├── Shared/Components/          # MetricsCard, Charts, Table, StatusIndicator
│   └── wwwroot/                    # Dark theme CSS, Chart.js interop
├── RealTimeDashboard.Tests/        # Unit tests + load tests
docs/
├── architecture/                   # ARCHITECTURE.md, diagrams, benchmarks
├── phases/                         # Development phase specifications
└── blog/                           # Blog article draft
```

## Scaling Paths

| Scale | Changes Needed |
|-------|---------------|
| **100K/day** (current) | Single instance, IMemoryCache, MySQL |
| **500K/day** | MySQL date partitioning |
| **1M+/day** | Redis cache + SignalR backplane, MySQL read replica |
| **10M+/day** | Kafka, Azure SignalR Service, ClickHouse |

## Configuration

| Environment Variable | Default | Description |
|---------------------|---------|-------------|
| `ConnectionStrings__DefaultConnection` | (required) | MySQL connection string |
| `TransactionProcessor__TargetTPS` | `10` | Transactions per second to generate |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Environment name |

## License

[MIT](LICENSE)
