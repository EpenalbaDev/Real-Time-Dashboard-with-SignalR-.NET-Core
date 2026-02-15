using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using RealTimeDashboard.Models;

namespace RealTimeDashboard.Tests.LoadTests;

[Trait("Category", "LoadTest")]
public class SignalRLoadTest : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SignalRLoadTest(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override TPS for load testing
                builder.UseSetting("TransactionProcessor:TargetTPS", "50");
            });
        });
    }

    [Fact]
    public async Task DiagnosticsEndpoint_ReturnsMetrics()
    {
        var client = _factory.CreateClient();

        // Let the app warm up
        await Task.Delay(2000);

        var response = await client.GetAsync("/api/diagnostics");
        response.EnsureSuccessStatusCode();

        var diagnostics = await response.Content.ReadFromJsonAsync<DiagnosticsDto>();
        Assert.NotNull(diagnostics);
        Assert.True(diagnostics.Timestamp > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task SingleConnection_ReceivesTransactions()
    {
        var server = _factory.Server;
        var hubUrl = new Uri(server.BaseAddress, "/hubs/dashboard");

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();

        var receivedBatches = 0;
        var totalTransactions = 0;

        connection.On<List<TransactionDto>>("ReceiveTransactionBatch", batch =>
        {
            Interlocked.Increment(ref receivedBatches);
            Interlocked.Add(ref totalTransactions, batch.Count);
        });

        await connection.StartAsync();
        Assert.Equal(HubConnectionState.Connected, connection.State);

        // Wait for a few broadcast cycles
        await Task.Delay(3000);

        await connection.StopAsync();
        await connection.DisposeAsync();

        Assert.True(receivedBatches > 0, "Should have received at least one batch");
        Assert.True(totalTransactions > 0, "Should have received transactions");
    }

    [Fact]
    public async Task MultipleConnections_AllReceiveData()
    {
        const int connectionCount = 10;
        var server = _factory.Server;
        var hubUrl = new Uri(server.BaseAddress, "/hubs/dashboard");

        var connections = new List<HubConnection>();
        var batchesPerConnection = new int[connectionCount];

        for (var i = 0; i < connectionCount; i++)
        {
            var idx = i;
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                })
                .Build();

            connection.On<List<TransactionDto>>("ReceiveTransactionBatch", _ =>
            {
                Interlocked.Increment(ref batchesPerConnection[idx]);
            });

            connections.Add(connection);
        }

        // Start all connections in parallel
        await Task.WhenAll(connections.Select(c => c.StartAsync()));

        // Wait for broadcasts
        await Task.Delay(3000);

        // Stop all connections
        await Task.WhenAll(connections.Select(c => c.StopAsync()));
        foreach (var c in connections)
            await c.DisposeAsync();

        // Verify all connections received data
        for (var i = 0; i < connectionCount; i++)
        {
            Assert.True(batchesPerConnection[i] > 0,
                $"Connection {i} should have received at least one batch");
        }
    }

    [Fact]
    public async Task BroadcastLatency_WithinTarget()
    {
        var server = _factory.Server;
        var hubUrl = new Uri(server.BaseAddress, "/hubs/dashboard");

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();

        var latencies = new List<double>();
        var sw = Stopwatch.StartNew();

        connection.On<List<TransactionDto>>("ReceiveTransactionBatch", _ =>
        {
            latencies.Add(sw.Elapsed.TotalMilliseconds);
            sw.Restart();
        });

        await connection.StartAsync();

        // Collect latency samples
        await Task.Delay(5000);

        await connection.StopAsync();
        await connection.DisposeAsync();

        if (latencies.Count > 1)
        {
            // Skip first measurement (warm-up)
            var validLatencies = latencies.Skip(1).ToList();
            var avgLatency = validLatencies.Average();
            var maxLatency = validLatencies.Max();

            // Broadcast interval is 500ms, so inter-batch time should be close to that
            Assert.True(avgLatency < 1500,
                $"Average inter-batch interval should be under 1500ms, was {avgLatency:F2}ms");
        }
    }

    [Fact]
    public async Task MetricsUpdate_ReceivedPeriodically()
    {
        var server = _factory.Server;
        var hubUrl = new Uri(server.BaseAddress, "/hubs/dashboard");

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            })
            .Build();

        var metricsCount = 0;
        DashboardMetricsDto? lastMetrics = null;

        connection.On<DashboardMetricsDto>("ReceiveMetricsUpdate", metrics =>
        {
            Interlocked.Increment(ref metricsCount);
            lastMetrics = metrics;
        });

        await connection.StartAsync();
        await Task.Delay(3000);

        await connection.StopAsync();
        await connection.DisposeAsync();

        Assert.True(metricsCount > 0, "Should have received metrics updates");
        Assert.NotNull(lastMetrics);
        Assert.True(lastMetrics.ActiveConnections >= 0);
    }

    [Fact]
    public async Task StressTest_ConcurrentConnections_NoErrors()
    {
        const int connectionCount = 50;
        var server = _factory.Server;
        var hubUrl = new Uri(server.BaseAddress, "/hubs/dashboard");

        var connections = new List<HubConnection>();
        var errors = 0;
        var totalBatches = 0;

        for (var i = 0; i < connectionCount; i++)
        {
            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                })
                .Build();

            connection.On<List<TransactionDto>>("ReceiveTransactionBatch", _ =>
            {
                Interlocked.Increment(ref totalBatches);
            });

            connection.Closed += ex =>
            {
                if (ex != null) Interlocked.Increment(ref errors);
                return Task.CompletedTask;
            };

            connections.Add(connection);
        }

        // Connect in batches of 10
        for (var batch = 0; batch < connectionCount; batch += 10)
        {
            var batchConnections = connections.Skip(batch).Take(10);
            await Task.WhenAll(batchConnections.Select(c => c.StartAsync()));
            await Task.Delay(100); // Small delay between batches
        }

        // Run under load
        await Task.Delay(5000);

        // Disconnect all
        await Task.WhenAll(connections.Select(c => c.StopAsync()));
        foreach (var c in connections)
            await c.DisposeAsync();

        Assert.Equal(0, errors);
        Assert.True(totalBatches > 0,
            $"Should have received batches across {connectionCount} connections");
    }
}
