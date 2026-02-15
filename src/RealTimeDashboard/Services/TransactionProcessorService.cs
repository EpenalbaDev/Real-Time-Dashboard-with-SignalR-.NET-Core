using RealTimeDashboard.Data;
using RealTimeDashboard.Data.Entities;

namespace RealTimeDashboard.Services;

public sealed class TransactionProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TransactionChannel _broadcastChannel;
    private readonly ILogger<TransactionProcessorService> _logger;
    private readonly IConfiguration _configuration;

    private readonly List<TransactionEntity> _dbBatch = new(100);
    private readonly object _batchLock = new();

    private static readonly string[] Sources = ["ATM", "POS", "Online", "Transfer", "Mobile"];
    private static readonly string[] Descriptions =
    [
        "Monthly subscription", "Grocery purchase", "Online order", "Salary deposit",
        "Utility bill", "Restaurant payment", "ATM withdrawal", "Peer transfer",
        "Insurance premium", "Investment", "Refund", "Gas station",
        "Streaming service", "Freelance payment", "Loan repayment"
    ];

    public TransactionProcessorService(
        IServiceScopeFactory scopeFactory,
        TransactionChannel broadcastChannel,
        ILogger<TransactionProcessorService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _broadcastChannel = broadcastChannel;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TransactionProcessorService starting");

        var producerTask = ProduceTransactionsAsync(stoppingToken);
        var writerTask = PeriodicFlushAsync(stoppingToken);

        await Task.WhenAll(producerTask, writerTask);

        _logger.LogInformation("TransactionProcessorService stopped");
    }

    private async Task ProduceTransactionsAsync(CancellationToken stoppingToken)
    {
        var tps = _configuration.GetValue("TransactionProcessor:TargetTPS", 10);
        var delayMs = 1000 / tps;
        var random = new Random();

        _logger.LogInformation("Transaction producer started at {TPS} TPS", tps);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var transaction = GenerateTransaction(random);

                // Add to DB batch
                lock (_batchLock)
                {
                    _dbBatch.Add(transaction);
                }

                // Send to broadcast channel (non-blocking for broadcaster)
                await _broadcastChannel.Writer.WriteAsync(transaction, stoppingToken);

                // Simulate async processing lifecycle
                _ = SimulateProcessingAsync(transaction, random, stoppingToken);

                await Task.Delay(delayMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error producing transaction");
                await Task.Delay(1000, stoppingToken);
            }
        }

        // Final flush
        await FlushBatchAsync();
        _broadcastChannel.Writer.TryComplete();
    }

    private async Task PeriodicFlushAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Batch database writer started");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await FlushBatchAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing batch to database");
            }
        }
    }

    private async Task FlushBatchAsync()
    {
        List<TransactionEntity> toFlush;

        lock (_batchLock)
        {
            if (_dbBatch.Count == 0) return;
            toFlush = new List<TransactionEntity>(_dbBatch);
            _dbBatch.Clear();
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.Transactions.AddRange(toFlush);
        await context.SaveChangesAsync();

        _logger.LogDebug("Flushed {Count} transactions to database", toFlush.Count);
    }

    private async Task SimulateProcessingAsync(TransactionEntity transaction, Random random, CancellationToken stoppingToken)
    {
        try
        {
            var processingDelay = random.Next(100, 2000);
            await Task.Delay(processingDelay, stoppingToken);

            transaction.Status = TransactionStatus.Processing;

            await Task.Delay(random.Next(50, 500), stoppingToken);

            var roll = random.Next(100);
            transaction.Status = roll switch
            {
                < 85 => TransactionStatus.Completed,
                < 95 => TransactionStatus.Failed,
                _ => TransactionStatus.Flagged
            };
            transaction.ProcessedAt = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
    }

    private static TransactionEntity GenerateTransaction(Random random)
    {
        var type = PickTransactionType(random);
        var amount = GenerateRealisticAmount(random);

        return new TransactionEntity
        {
            TransactionId = Guid.NewGuid().ToString(),
            Amount = amount,
            Currency = "USD",
            Type = type,
            Status = TransactionStatus.Pending,
            Source = Sources[random.Next(Sources.Length)],
            Description = Descriptions[random.Next(Descriptions.Length)],
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static TransactionType PickTransactionType(Random random)
    {
        var roll = random.Next(100);
        return roll switch
        {
            < 40 => TransactionType.Payment,
            < 70 => TransactionType.Transfer,
            < 85 => TransactionType.Deposit,
            < 95 => TransactionType.Withdrawal,
            _ => TransactionType.Refund
        };
    }

    private static decimal GenerateRealisticAmount(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        var normalRandom = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

        var amount = 250.0 + normalRandom * 500.0;
        amount = Math.Max(1.0, Math.Min(50_000.0, Math.Abs(amount)));

        return Math.Round((decimal)amount, 2);
    }
}
