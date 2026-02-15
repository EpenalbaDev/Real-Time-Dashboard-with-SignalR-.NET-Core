# Phase 1: Foundation

## Objective
Scaffold the Blazor Server project, configure MySQL with EF Core, create domain entities, and seed historical data.

## Prerequisites
- .NET 8 SDK
- MySQL 8.0 (local or Docker: `docker run -d -p 3306:3306 -e MYSQL_ROOT_PASSWORD=dev123 -e MYSQL_DATABASE=dashboard_db mysql:8.0`)
- Node.js (for Chart.js, optional — can use CDN)

## Tasks

### 1.1 Create Solution & Project
```bash
dotnet new sln -n RealTimeDashboard
dotnet new blazorserver -n RealTimeDashboard -o src/RealTimeDashboard --framework net8.0
dotnet new xunit -n RealTimeDashboard.Tests -o src/RealTimeDashboard.Tests
dotnet sln add src/RealTimeDashboard
dotnet sln add src/RealTimeDashboard.Tests
```

### 1.2 Install NuGet Packages
```bash
cd src/RealTimeDashboard
dotnet add package Pomelo.EntityFrameworkCore.MySql --version 8.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.*
dotnet add package Microsoft.EntityFrameworkCore.Tools --version 8.*
```

### 1.3 Create Entities

**Transaction Entity:**
```csharp
public sealed record TransactionEntity
{
    public long Id { get; init; }
    public string TransactionId { get; init; } = string.Empty; // GUID-based external ID
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
    public TransactionType Type { get; init; }
    public TransactionStatus Status { get; init; }
    public string Source { get; init; } = string.Empty;      // e.g., "ATM", "POS", "Online", "Transfer"
    public string? Description { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
}

public enum TransactionType { Deposit, Withdrawal, Transfer, Payment, Refund }
public enum TransactionStatus { Pending, Processing, Completed, Failed, Flagged }
```

**DashboardMetric Entity (pre-computed):**
```csharp
public sealed record DashboardMetric
{
    public int Id { get; init; }
    public string MetricName { get; init; } = string.Empty;
    public decimal Value { get; init; }
    public string Period { get; init; } = string.Empty;     // "1min", "5min", "1hour", "1day"
    public DateTimeOffset ComputedAt { get; init; }
}
```

### 1.4 Configure DbContext
- Pomelo MySQL provider
- Fluent API configuration (no data annotations)
- Indexes on CreatedAt, Status, Type
- Connection string from `appsettings.json` → environment variable override for Railway

### 1.5 Seed Data
Create `DataSeeder.cs`:
- Generate 10,000 historical transactions spread over last 30 days
- Realistic distribution: 60% completed, 15% pending, 10% processing, 10% failed, 5% flagged
- Amount range: $1 - $50,000 with realistic bell curve
- Mix of transaction types weighted toward Payment (40%) and Transfer (30%)

### 1.6 Create Base Service Layer
- `ITransactionService` with basic CRUD
- `TransactionService` implementation with EF Core
- Register in DI

## Definition of Done
- [ ] `dotnet build` succeeds
- [ ] `dotnet ef database update` creates schema in MySQL
- [ ] Seed data populates 10K transactions
- [ ] Basic `/transactions` page shows paginated list
- [ ] Unit tests pass for TransactionService
- [ ] Git: commit on `phase/1-foundation`, tag `v0.1.0`

## Estimated Time: 2-3 hours with Claude Code
