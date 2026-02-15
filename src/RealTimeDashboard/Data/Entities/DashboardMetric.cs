namespace RealTimeDashboard.Data.Entities;

public sealed class DashboardMetric
{
    public int Id { get; set; }
    public string MetricName { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string Period { get; set; } = string.Empty;
    public DateTimeOffset ComputedAt { get; set; }
}
