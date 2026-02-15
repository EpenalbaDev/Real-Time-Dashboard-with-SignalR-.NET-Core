using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RealTimeDashboard.Data;
using RealTimeDashboard.Data.Entities;
using RealTimeDashboard.Services;

namespace RealTimeDashboard.Tests.Services;

public sealed class MetricsAggregatorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly MetricsAggregator _aggregator;

    public MetricsAggregatorTests()
    {
        var services = new ServiceCollection();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        _serviceProvider = services.BuildServiceProvider();

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<MetricsAggregator>>();

        _aggregator = new MetricsAggregator(scopeFactory, cache, logger);
    }

    [Fact]
    public async Task ComputeMetrics_EmptyDatabase_ReturnsZeroMetrics()
    {
        var metrics = await _aggregator.ComputeMetricsAsync();

        Assert.Equal(0, metrics.TotalTransactions1Min);
        Assert.Equal(0, metrics.TotalTransactions5Min);
        Assert.Equal(0, metrics.TotalTransactions1Hour);
        Assert.Equal(0m, metrics.TotalVolume1Min);
        Assert.Equal(0, metrics.SuccessRate);
        Assert.Equal(0, metrics.FailureRate);
        Assert.Equal(0, metrics.FlaggedCount);
        Assert.Empty(metrics.TopSources);
    }

    [Fact]
    public async Task ComputeMetrics_WithRecentTransactions_CountsCorrectly()
    {
        await SeedTransactionsAsync(now: DateTimeOffset.UtcNow, count: 5);

        var metrics = await _aggregator.ComputeMetricsAsync();

        Assert.Equal(5, metrics.TotalTransactions1Min);
        Assert.Equal(5, metrics.TotalTransactions5Min);
        Assert.Equal(5, metrics.TotalTransactions1Hour);
    }

    [Fact]
    public async Task ComputeMetrics_DifferentTimeBuckets_SegmentsCorrectly()
    {
        var now = DateTimeOffset.UtcNow;

        // 3 transactions within 1 minute
        await SeedTransactionsAsync(now: now.AddSeconds(-10), count: 3);
        // 2 transactions 3 minutes ago (within 5min but not 1min)
        await SeedTransactionsAsync(now: now.AddMinutes(-3), count: 2);
        // 4 transactions 30 minutes ago (within 1hour but not 5min)
        await SeedTransactionsAsync(now: now.AddMinutes(-30), count: 4);

        var metrics = await _aggregator.ComputeMetricsAsync();

        Assert.Equal(3, metrics.TotalTransactions1Min);
        Assert.Equal(5, metrics.TotalTransactions5Min);
        Assert.Equal(9, metrics.TotalTransactions1Hour);
    }

    [Fact]
    public async Task ComputeMetrics_OldTransactions_ExcludedFromAllBuckets()
    {
        // Transactions from 2 hours ago — should not appear
        await SeedTransactionsAsync(now: DateTimeOffset.UtcNow.AddHours(-2), count: 10);

        var metrics = await _aggregator.ComputeMetricsAsync();

        Assert.Equal(0, metrics.TotalTransactions1Min);
        Assert.Equal(0, metrics.TotalTransactions5Min);
        Assert.Equal(0, metrics.TotalTransactions1Hour);
    }

    [Fact]
    public async Task ComputeMetrics_SuccessAndFailureRates_CalculatedCorrectly()
    {
        var now = DateTimeOffset.UtcNow;

        // 8 completed, 2 failed = 80% success, 20% failure
        await SeedTransactionsAsync(now, 8, status: TransactionStatus.Completed);
        await SeedTransactionsAsync(now, 2, status: TransactionStatus.Failed);

        var metrics = await _aggregator.ComputeMetricsAsync();

        Assert.Equal(80.0, metrics.SuccessRate);
        Assert.Equal(20.0, metrics.FailureRate);
    }

    [Fact]
    public async Task ComputeMetrics_PendingTransactions_NotIncludedInRates()
    {
        var now = DateTimeOffset.UtcNow;

        // 6 completed, 4 pending => rates calculated from completed+failed only
        await SeedTransactionsAsync(now, 6, status: TransactionStatus.Completed);
        await SeedTransactionsAsync(now, 4, status: TransactionStatus.Pending);

        var metrics = await _aggregator.ComputeMetricsAsync();

        // Only 6 completed out of 6 finished (pending doesn't count)
        Assert.Equal(100.0, metrics.SuccessRate);
        Assert.Equal(0.0, metrics.FailureRate);
    }

    [Fact]
    public async Task ComputeMetrics_FlaggedCount_CalculatedCorrectly()
    {
        var now = DateTimeOffset.UtcNow;

        await SeedTransactionsAsync(now, 10, status: TransactionStatus.Completed);
        await SeedTransactionsAsync(now, 3, status: TransactionStatus.Flagged);

        var metrics = await _aggregator.ComputeMetricsAsync();

        Assert.Equal(3, metrics.FlaggedCount);
    }

    [Fact]
    public async Task ComputeMetrics_Volume_SumsCorrectly()
    {
        var now = DateTimeOffset.UtcNow;

        // Each seeded txn has amount = 100m
        await SeedTransactionsAsync(now, 5, amount: 100m);

        var metrics = await _aggregator.ComputeMetricsAsync();

        Assert.Equal(500m, metrics.TotalVolume1Min);
        Assert.Equal(500m, metrics.TotalVolume5Min);
        Assert.Equal(500m, metrics.TotalVolume1Hour);
    }

    [Fact]
    public async Task ComputeMetrics_TopSources_ReturnsTop5Ordered()
    {
        var now = DateTimeOffset.UtcNow;

        await SeedTransactionsAsync(now, 10, source: "Online");
        await SeedTransactionsAsync(now, 7, source: "POS");
        await SeedTransactionsAsync(now, 5, source: "ATM");
        await SeedTransactionsAsync(now, 3, source: "Mobile");
        await SeedTransactionsAsync(now, 2, source: "Transfer");
        await SeedTransactionsAsync(now, 1, source: "Other");

        var metrics = await _aggregator.ComputeMetricsAsync();

        Assert.Equal(5, metrics.TopSources.Count);
        Assert.Equal("Online", metrics.TopSources[0].Source);
        Assert.Equal(10, metrics.TopSources[0].Count);
        Assert.Equal("POS", metrics.TopSources[1].Source);
    }

    [Fact]
    public async Task ComputeMetrics_AverageProcessingTime_CalculatedCorrectly()
    {
        var now = DateTimeOffset.UtcNow;

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Transaction processed in 500ms
        context.Transactions.Add(new TransactionEntity
        {
            TransactionId = Guid.NewGuid().ToString(),
            Amount = 100m,
            Currency = "USD",
            Type = TransactionType.Payment,
            Status = TransactionStatus.Completed,
            Source = "Online",
            CreatedAt = now.AddMilliseconds(-500),
            ProcessedAt = now
        });

        // Transaction processed in 1000ms
        context.Transactions.Add(new TransactionEntity
        {
            TransactionId = Guid.NewGuid().ToString(),
            Amount = 200m,
            Currency = "USD",
            Type = TransactionType.Payment,
            Status = TransactionStatus.Completed,
            Source = "POS",
            CreatedAt = now.AddMilliseconds(-1000),
            ProcessedAt = now
        });

        await context.SaveChangesAsync();

        var metrics = await _aggregator.ComputeMetricsAsync();

        // Average of 500ms and 1000ms = 750ms
        Assert.Equal(750.0, metrics.AverageProcessingTimeMs);
    }

    [Fact]
    public void GetCachedMetrics_BeforeCompute_ReturnsNull()
    {
        var result = _aggregator.GetCachedMetrics();
        Assert.Null(result);
    }

    private async Task SeedTransactionsAsync(
        DateTimeOffset now,
        int count,
        TransactionStatus status = TransactionStatus.Completed,
        string source = "Online",
        decimal amount = 100m)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        for (var i = 0; i < count; i++)
        {
            context.Transactions.Add(new TransactionEntity
            {
                TransactionId = Guid.NewGuid().ToString(),
                Amount = amount,
                Currency = "USD",
                Type = TransactionType.Payment,
                Status = status,
                Source = source,
                CreatedAt = now.AddSeconds(-i),
                ProcessedAt = status is TransactionStatus.Completed or TransactionStatus.Failed
                    ? now.AddSeconds(-i).AddMilliseconds(500)
                    : null
            });
        }

        await context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}
