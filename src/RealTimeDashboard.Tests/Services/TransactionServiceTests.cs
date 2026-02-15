using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RealTimeDashboard.Data;
using RealTimeDashboard.Data.Entities;
using RealTimeDashboard.Services;

namespace RealTimeDashboard.Tests.Services;

public sealed class TransactionServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly TransactionService _service;

    public TransactionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        var logger = Mock.Of<ILogger<TransactionService>>();
        _service = new TransactionService(_context, logger);
    }

    [Fact]
    public async Task GetTransactionsAsync_ReturnsPagedResults()
    {
        // Arrange
        await SeedTransactionsAsync(50);

        // Act
        var result = await _service.GetTransactionsAsync(page: 1, pageSize: 25);

        // Assert
        Assert.Equal(25, result.Items.Count);
        Assert.Equal(50, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.TotalPages);
        Assert.True(result.HasNextPage);
        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public async Task GetTransactionsAsync_SecondPage_ReturnsRemainingItems()
    {
        // Arrange
        await SeedTransactionsAsync(50);

        // Act
        var result = await _service.GetTransactionsAsync(page: 2, pageSize: 25);

        // Assert
        Assert.Equal(25, result.Items.Count);
        Assert.Equal(2, result.Page);
        Assert.False(result.HasNextPage);
        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public async Task GetTransactionsAsync_FilterByStatus_ReturnsFilteredResults()
    {
        // Arrange
        await SeedTransactionsAsync(10, status: TransactionStatus.Completed);
        await SeedTransactionsAsync(5, status: TransactionStatus.Pending);

        // Act
        var result = await _service.GetTransactionsAsync(statusFilter: TransactionStatus.Completed);

        // Assert
        Assert.Equal(10, result.TotalCount);
        Assert.All(result.Items, t => Assert.Equal(TransactionStatus.Completed, t.Status));
    }

    [Fact]
    public async Task GetTransactionsAsync_FilterByType_ReturnsFilteredResults()
    {
        // Arrange
        await SeedTransactionsAsync(8, type: TransactionType.Payment);
        await SeedTransactionsAsync(4, type: TransactionType.Transfer);

        // Act
        var result = await _service.GetTransactionsAsync(typeFilter: TransactionType.Payment);

        // Assert
        Assert.Equal(8, result.TotalCount);
        Assert.All(result.Items, t => Assert.Equal(TransactionType.Payment, t.Type));
    }

    [Fact]
    public async Task GetTransactionsAsync_OrdersByCreatedAtDescending()
    {
        // Arrange
        await SeedTransactionsAsync(10);

        // Act
        var result = await _service.GetTransactionsAsync();

        // Assert
        for (var i = 1; i < result.Items.Count; i++)
        {
            Assert.True(result.Items[i - 1].CreatedAt >= result.Items[i].CreatedAt);
        }
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsTransaction()
    {
        // Arrange
        await SeedTransactionsAsync(1);
        var seeded = await _context.Transactions.FirstAsync();

        // Act
        var result = await _service.GetByIdAsync(seeded.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(seeded.TransactionId, result.TransactionId);
        Assert.Equal(seeded.Amount, result.Amount);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingId_ReturnsNull()
    {
        // Act
        var result = await _service.GetByIdAsync(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByTransactionIdAsync_ExistingId_ReturnsTransaction()
    {
        // Arrange
        await SeedTransactionsAsync(1);
        var seeded = await _context.Transactions.FirstAsync();

        // Act
        var result = await _service.GetByTransactionIdAsync(seeded.TransactionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(seeded.Id, result.Id);
    }

    [Fact]
    public async Task GetByTransactionIdAsync_NonExistingId_ReturnsNull()
    {
        // Act
        var result = await _service.GetByTransactionIdAsync("non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_AddsTransactionAndReturnsDto()
    {
        // Arrange
        var entity = new TransactionEntity
        {
            TransactionId = Guid.NewGuid().ToString(),
            Amount = 100.50m,
            Currency = "USD",
            Type = TransactionType.Payment,
            Status = TransactionStatus.Pending,
            Source = "Online",
            Description = "Test transaction",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _service.CreateAsync(entity);

        // Assert
        Assert.Equal(entity.TransactionId, result.TransactionId);
        Assert.Equal(100.50m, result.Amount);
        Assert.Equal(TransactionType.Payment, result.Type);
        Assert.Equal(TransactionStatus.Pending, result.Status);

        var count = await _context.Transactions.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetTotalCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await SeedTransactionsAsync(15);

        // Act
        var count = await _service.GetTotalCountAsync();

        // Assert
        Assert.Equal(15, count);
    }

    [Fact]
    public async Task GetTransactionsAsync_EmptyDatabase_ReturnsEmptyResult()
    {
        // Act
        var result = await _service.GetTransactionsAsync();

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private async Task SeedTransactionsAsync(
        int count,
        TransactionStatus status = TransactionStatus.Completed,
        TransactionType type = TransactionType.Payment)
    {
        var baseTime = DateTimeOffset.UtcNow;

        for (var i = 0; i < count; i++)
        {
            _context.Transactions.Add(new TransactionEntity
            {
                TransactionId = Guid.NewGuid().ToString(),
                Amount = 10m + i,
                Currency = "USD",
                Type = type,
                Status = status,
                Source = "Test",
                Description = $"Test transaction {i}",
                CreatedAt = baseTime.AddMinutes(-i)
            });
        }

        await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
