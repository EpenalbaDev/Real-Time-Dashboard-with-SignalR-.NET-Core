using Microsoft.EntityFrameworkCore;
using RealTimeDashboard.Data.Entities;

namespace RealTimeDashboard.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<TransactionEntity> Transactions => Set<TransactionEntity>();
    public DbSet<DashboardMetric> DashboardMetrics => Set<DashboardMetric>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TransactionEntity>(entity =>
        {
            entity.ToTable("Transactions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.TransactionId)
                .IsRequired()
                .HasMaxLength(36);

            entity.Property(e => e.Amount)
                .HasPrecision(18, 2);

            entity.Property(e => e.Currency)
                .IsRequired()
                .HasMaxLength(3);

            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Source)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Description)
                .HasMaxLength(500);

            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("IX_Transaction_CreatedAt");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_Transaction_Status");

            entity.HasIndex(e => e.Type)
                .HasDatabaseName("IX_Transaction_Type");

            entity.HasIndex(e => e.TransactionId)
                .IsUnique();
        });

        modelBuilder.Entity<DashboardMetric>(entity =>
        {
            entity.ToTable("DashboardMetrics");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.MetricName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Value)
                .HasPrecision(18, 2);

            entity.Property(e => e.Period)
                .IsRequired()
                .HasMaxLength(20);

            entity.HasIndex(e => new { e.MetricName, e.Period })
                .HasDatabaseName("IX_DashboardMetric_Name_Period");
        });
    }
}
