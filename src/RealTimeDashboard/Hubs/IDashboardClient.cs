using RealTimeDashboard.Models;

namespace RealTimeDashboard.Hubs;

public interface IDashboardClient
{
    Task ReceiveTransactionBatch(IReadOnlyList<TransactionDto> transactions);
    Task ReceiveMetricsUpdate(DashboardMetricsDto metrics);
    Task ReceiveAlert(AlertDto alert);
}
