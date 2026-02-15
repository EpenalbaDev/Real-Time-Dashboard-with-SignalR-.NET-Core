# CLAUDE.md — Real-Time Dashboard with SignalR + .NET Core

## Project Overview
Real-time financial transaction dashboard demonstrating enterprise-grade architecture.
**Goal:** Public GitHub repo + technical blog article showing how to scale SignalR to 100K+ daily transactions.

## Tech Stack
- **Backend:** .NET 8, Blazor Server, SignalR (built-in)
- **Database:** MySQL 8.0 (EF Core + Pomelo provider)
- **Charts:** Chart.js via Blazor JS Interop
- **Caching:** In-memory cache (IMemoryCache) → Redis upgrade path documented
- **Deploy:** Railway (Docker container + MySQL managed)
- **Testing:** xUnit + bUnit

## Architecture Principles
- CQRS-lite: Separate read/write paths for transactions
- Hub pattern: Single SignalR hub with typed client interface
- Background service: TransactionProcessorService generates simulated transactions
- Aggregation pipeline: Pre-compute dashboard metrics, don't query raw data on each refresh
- Backpressure: Batch SignalR updates (every 500ms, not per-transaction)

## Project Structure
```
src/
├── RealTimeDashboard/                 # Blazor Server app
│   ├── Hubs/                          # SignalR hubs
│   │   └── DashboardHub.cs
│   ├── Services/                      # Business logic
│   │   ├── TransactionProcessor.cs    # Background service - generates/processes txns
│   │   ├── MetricsAggregator.cs       # Pre-computes dashboard metrics
│   │   └── TransactionService.cs      # CRUD operations
│   ├── Data/                          # EF Core
│   │   ├── AppDbContext.cs
│   │   ├── Entities/
│   │   └── Migrations/
│   ├── Models/                        # DTOs, ViewModels
│   ├── Components/                    # Blazor components
│   │   ├── Pages/
│   │   │   ├── Dashboard.razor        # Main dashboard page
│   │   │   └── TransactionDetail.razor
│   │   ├── Layout/
│   │   └── Shared/
│   │       ├── TransactionChart.razor  # Chart.js wrapper
│   │       ├── MetricsCard.razor
│   │       ├── TransactionTable.razor
│   │       └── StatusIndicator.razor
│   ├── wwwroot/
│   │   ├── js/
│   │   │   └── chartInterop.js        # Chart.js interop
│   │   └── css/
│   ├── Program.cs
│   └── appsettings.json
├── RealTimeDashboard.Tests/           # Unit + integration tests
│   ├── Services/
│   ├── Hubs/
│   └── Components/
docs/
├── architecture/
│   ├── ARCHITECTURE.md                # Full architecture doc (blog content source)
│   ├── diagrams/                      # Mermaid diagrams
│   │   ├── system-overview.mmd
│   │   ├── signalr-flow.mmd
│   │   ├── data-pipeline.mmd
│   │   └── scaling-strategy.mmd
│   └── benchmarks/                    # Performance numbers
│       └── BENCHMARKS.md
├── phases/                            # Development phases
│   ├── PHASE-1-FOUNDATION.md
│   ├── PHASE-2-REALTIME.md
│   ├── PHASE-3-DASHBOARD-UI.md
│   ├── PHASE-4-SCALE.md
│   └── PHASE-5-DEPLOY-BLOG.md
└── blog/
    └── ARTICLE-DRAFT.md               # Blog article draft
```

## Coding Standards
- Use `sealed` on classes that won't be inherited
- Nullable reference types enabled
- Use `record` for DTOs and immutable models
- Async all the way — no `.Result` or `.Wait()`
- Use `ILogger<T>` everywhere, structured logging
- No magic strings — constants or enums
- XML docs on public APIs only

## SignalR Specific Rules
- NEVER send updates per-transaction. Always batch (500ms minimum interval)
- Use `IHubContext<DashboardHub>` in services, never inject Hub directly
- Typed hub interface: `IDashboardClient` with strongly-typed methods
- Group connections by dashboard type if needed
- Handle reconnection gracefully in Blazor components

## Database Rules
- Use `DateTimeOffset` for all timestamps (UTC)
- Index: `IX_Transaction_CreatedAt`, `IX_Transaction_Status`, `IX_Transaction_Type`
- Partition strategy documented but not implemented (blog discusses it)
- Seed data: 10K historical transactions for demo

## Performance Targets (documented in benchmarks)
- Dashboard refresh: < 100ms for aggregated metrics
- SignalR broadcast latency: < 50ms hub-to-client
- Concurrent connections: 500+ on single instance
- Transaction throughput: 100+ TPS simulated
- Memory: < 512MB under load

## Git Conventions
- Conventional commits: `feat:`, `fix:`, `docs:`, `perf:`, `test:`
- Branch per phase: `phase/1-foundation`, `phase/2-realtime`, etc.
- Tag releases: `v0.1.0` (Phase 1), `v0.2.0` (Phase 2), etc.

## What NOT to Do
- Don't add authentication (out of scope, mention in blog as "production would add...")
- Don't use Redis yet (document as scaling path, use IMemoryCache)
- Don't over-engineer DI — keep it simple, this is a demo
- Don't add Kubernetes configs — Railway Docker is enough
- Don't use Blazor WebAssembly (Server is the right choice for SignalR demo)
