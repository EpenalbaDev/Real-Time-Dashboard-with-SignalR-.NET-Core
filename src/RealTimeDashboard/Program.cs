using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using RealTimeDashboard.Data;
using RealTimeDashboard.Hubs;
using RealTimeDashboard.Models;
using RealTimeDashboard.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// Response compression for SignalR
builder.Services.AddResponseCompression(options =>
{
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/octet-stream"]);
});

// SignalR
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 64 * 1024; // 64KB
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// Caching
builder.Services.AddMemoryCache();

// Database
// Support Railway's MYSQL_URL or standard connection string
var connectionString = Environment.GetEnvironmentVariable("MYSQL_URL") is { } mysqlUrl
    ? ConvertMySqlUrl(mysqlUrl)
    : builder.Configuration.GetConnectionString("DefaultConnection")
      ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

var serverVersion = new MySqlServerVersion(new Version(9, 0, 0));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, serverVersion, mysqlOptions =>
    {
        mysqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
    }));

// Services
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddSingleton<TransactionChannel>();
builder.Services.AddSingleton<MetricsAggregator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MetricsAggregator>());
builder.Services.AddSingleton<TransactionProcessorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TransactionProcessorService>());
builder.Services.AddSingleton<DashboardBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DashboardBroadcaster>());
builder.Services.AddScoped<ChartJsInterop>();

var app = builder.Build();

// Apply migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await context.Database.MigrateAsync();
        await DataSeeder.SeedAsync(context, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating or seeding the database");
        throw;
    }
}

// Configure the HTTP request pipeline.
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();

app.MapHealthChecks("/health");
app.MapBlazorHub();
app.MapHub<DashboardHub>("/hubs/dashboard");

app.MapGet("/api/diagnostics", (
    DashboardBroadcaster broadcaster,
    MetricsAggregator aggregator,
    TransactionProcessorService processor) =>
{
    return Results.Ok(new DiagnosticsDto
    {
        Broadcaster = new BroadcasterDiagnostics(
            broadcaster.TotalBroadcasts,
            broadcaster.TotalTransactionsBroadcast,
            broadcaster.LastBroadcastMs,
            broadcaster.MaxBroadcastMs,
            broadcaster.AvgBroadcastMs),
        Aggregator = new AggregatorDiagnostics(
            aggregator.TotalComputations,
            aggregator.LastComputeMs,
            aggregator.MaxComputeMs,
            aggregator.AvgComputeMs),
        Processor = new ProcessorDiagnostics(
            processor.TotalTransactionsProduced,
            processor.TotalDbFlushes,
            processor.TotalDbRowsWritten,
            processor.LastFlushMs,
            processor.MaxFlushMs,
            processor.AvgFlushMs),
        ActiveConnections = DashboardHub.ConnectionCount,
        Timestamp = DateTimeOffset.UtcNow
    });
});

app.MapFallbackToPage("/_Host");

app.Run();

public partial class Program
{
    /// <summary>
    /// Converts a MySQL URL (mysql://user:pass@host:port/db) to a .NET connection string.
    /// Railway provides MYSQL_URL automatically for MySQL services.
    /// </summary>
    static string ConvertMySqlUrl(string url)
    {
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':');
        var user = userInfo[0];
        var password = userInfo.Length > 1 ? userInfo[1] : "";
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 3306;
        var database = uri.AbsolutePath.TrimStart('/');

        return $"Server={host};Port={port};Database={database};User={user};Password={password};";
    }
}
