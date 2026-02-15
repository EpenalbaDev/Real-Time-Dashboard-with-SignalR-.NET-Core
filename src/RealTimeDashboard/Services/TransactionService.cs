using Microsoft.EntityFrameworkCore;
using RealTimeDashboard.Data;
using RealTimeDashboard.Data.Entities;
using RealTimeDashboard.Models;

namespace RealTimeDashboard.Services;

public sealed class TransactionService : ITransactionService
{
    private readonly AppDbContext _context;
    private readonly ILogger<TransactionService> _logger;

    public TransactionService(AppDbContext context, ILogger<TransactionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PagedResult<TransactionDto>> GetTransactionsAsync(
        int page = 1,
        int pageSize = 25,
        TransactionStatus? statusFilter = null,
        TransactionType? typeFilter = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Transactions.AsNoTracking();

        if (statusFilter.HasValue)
            query = query.Where(t => t.Status == statusFilter.Value);

        if (typeFilter.HasValue)
            query = query.Where(t => t.Type == typeFilter.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => MapToDto(t))
            .ToListAsync(cancellationToken);

        _logger.LogDebug("Retrieved {Count} transactions (page {Page}/{TotalPages})",
            items.Count, page, (int)Math.Ceiling((double)totalCount / pageSize));

        return new PagedResult<TransactionDto>(items, totalCount, page, pageSize);
    }

    public async Task<TransactionDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return entity is null ? null : MapToDto(entity);
    }

    public async Task<TransactionDto?> GetByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken);

        return entity is null ? null : MapToDto(entity);
    }

    public async Task<TransactionDto> CreateAsync(TransactionEntity entity, CancellationToken cancellationToken = default)
    {
        _context.Transactions.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created transaction {TransactionId} with amount {Amount}",
            entity.TransactionId, entity.Amount);

        return MapToDto(entity);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Transactions.CountAsync(cancellationToken);
    }

    private static TransactionDto MapToDto(TransactionEntity entity)
    {
        return new TransactionDto(
            entity.Id,
            entity.TransactionId,
            entity.Amount,
            entity.Currency,
            entity.Type,
            entity.Status,
            entity.Source,
            entity.Description,
            entity.CreatedAt,
            entity.ProcessedAt);
    }
}
