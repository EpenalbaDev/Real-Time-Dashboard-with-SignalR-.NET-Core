using RealTimeDashboard.Data.Entities;

namespace RealTimeDashboard.Models;

public sealed record TransactionDto(
    long Id,
    string TransactionId,
    decimal Amount,
    string Currency,
    TransactionType Type,
    TransactionStatus Status,
    string Source,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
