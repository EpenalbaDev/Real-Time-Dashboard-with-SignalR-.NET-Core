using Microsoft.EntityFrameworkCore;
using RealTimeDashboard.Data.Entities;

namespace RealTimeDashboard.Data;

public static class DataSeeder
{
    private static readonly string[] Sources = ["ATM", "POS", "Online", "Transfer", "Mobile"];

    private static readonly string[] Descriptions =
    [
        "Monthly subscription payment",
        "Grocery store purchase",
        "Online retail order",
        "Salary deposit",
        "Utility bill payment",
        "Restaurant transaction",
        "ATM cash withdrawal",
        "Peer-to-peer transfer",
        "Insurance premium",
        "Investment deposit",
        "Refund for returned item",
        "Gas station purchase",
        "Streaming service payment",
        "Freelance payment received",
        "Loan repayment"
    ];

    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        if (await context.Transactions.AnyAsync())
        {
            logger.LogInformation("Database already seeded with {Count} transactions", await context.Transactions.CountAsync());
            return;
        }

        logger.LogInformation("Seeding database with 10,000 historical transactions...");

        var random = new Random(42); // Fixed seed for reproducibility
        var transactions = new List<TransactionEntity>(10_000);
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 10_000; i++)
        {
            var createdAt = now.AddDays(-random.Next(0, 30))
                               .AddHours(-random.Next(0, 24))
                               .AddMinutes(-random.Next(0, 60))
                               .AddSeconds(-random.Next(0, 60));

            var type = PickTransactionType(random);
            var status = PickTransactionStatus(random);
            var amount = GenerateRealisticAmount(random);

            var transaction = new TransactionEntity
            {
                TransactionId = Guid.NewGuid().ToString(),
                Amount = amount,
                Currency = "USD",
                Type = type,
                Status = status,
                Source = Sources[random.Next(Sources.Length)],
                Description = Descriptions[random.Next(Descriptions.Length)],
                CreatedAt = createdAt,
                ProcessedAt = status is TransactionStatus.Completed or TransactionStatus.Failed
                    ? createdAt.AddSeconds(random.Next(1, 300))
                    : null
            };

            transactions.Add(transaction);
        }

        context.Transactions.AddRange(transactions);
        await context.SaveChangesAsync();

        logger.LogInformation("Successfully seeded {Count} transactions", transactions.Count);
    }

    private static TransactionType PickTransactionType(Random random)
    {
        // Payment 40%, Transfer 30%, Deposit 15%, Withdrawal 10%, Refund 5%
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

    private static TransactionStatus PickTransactionStatus(Random random)
    {
        // Completed 60%, Pending 15%, Processing 10%, Failed 10%, Flagged 5%
        var roll = random.Next(100);
        return roll switch
        {
            < 60 => TransactionStatus.Completed,
            < 75 => TransactionStatus.Pending,
            < 85 => TransactionStatus.Processing,
            < 95 => TransactionStatus.Failed,
            _ => TransactionStatus.Flagged
        };
    }

    private static decimal GenerateRealisticAmount(Random random)
    {
        // Bell curve distribution: most transactions $10-$500, some up to $50,000
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        var normalRandom = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

        // Mean $250, StdDev $500, clamped to $1 - $50,000
        var amount = 250.0 + normalRandom * 500.0;
        amount = Math.Max(1.0, Math.Min(50_000.0, Math.Abs(amount)));

        return Math.Round((decimal)amount, 2);
    }
}
