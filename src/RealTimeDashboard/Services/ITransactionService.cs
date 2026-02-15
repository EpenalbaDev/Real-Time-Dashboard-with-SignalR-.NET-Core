using RealTimeDashboard.Data.Entities;
using RealTimeDashboard.Models;

namespace RealTimeDashboard.Services;

public interface ITransactionService
{
    Task<PagedResult<TransactionDto>> GetTransactionsAsync(
        int page = 1,
        int pageSize = 25,
        TransactionStatus? statusFilter = null,
        TransactionType? typeFilter = null,
        CancellationToken cancellationToken = default);

    Task<TransactionDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<TransactionDto?> GetByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default);

    Task<TransactionDto> CreateAsync(TransactionEntity entity, CancellationToken cancellationToken = default);

    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default);
}
