using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using McpKit.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;

namespace McpKit.Tests;

public sealed class ToolCallTelemetryOverMcpTests : IDisposable
{
    private const string AccountId = "acc-5120";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 20, 0, 0, TimeSpan.Zero));
    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly ActivityListener _listener;

    public ToolCallTelemetryOverMcpTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Calls_are_counted_per_tool_and_outcome()
    {
        await using var app = await StartHost();
        using var calls = Collect<long>(app, "mcpkit.tool.calls");
        await using var client = await Connect(app);

        await client.CallToolAsync("deposit", Deposit(25m), cancellationToken: Token);
        await client.CallToolAsync("deposit", Deposit(-5m), cancellationToken: Token);
        await client.CallToolAsync("close_account", new Dictionary<string, object?> { ["id"] = AccountId }, cancellationToken: Token);

        var measurements = calls.GetMeasurementSnapshot();
        Assert.Equal(
            ["deposit:succeeded", "deposit:failed", "close_account:confirmation_requested"],
            measurements.Select(m => $"{m.Tags["gen_ai.tool.name"]}:{m.Tags["mcpkit.tool.outcome"]}"));
        Assert.All(measurements, m => Assert.Equal(1, m.Value));
    }

    [Fact]
    public async Task The_duration_is_in_seconds_from_the_host_clock()
    {
        await using var app = await StartHost();
        using var durations = Collect<double>(app, "mcpkit.tool.duration");
        await using var client = await Connect(app);

        await client.CallToolAsync("deposit", Deposit(25m), cancellationToken: Token);

        var measurement = Assert.Single(durations.GetMeasurementSnapshot());
        Assert.Equal(0.25, measurement.Value);
        Assert.Equal("succeeded", measurement.Tags["mcpkit.tool.outcome"]);
    }

    [Fact]
    public async Task The_span_around_the_call_gets_the_outcome_and_the_endpoint_status()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);

        await client.CallToolAsync("deposit", Deposit(-5m), cancellationToken: Token);

        Assert.Contains(_spans, span =>
            Equals(span.GetTagItem("mcpkit.tool.outcome"), "failed")
            && Equals(span.GetTagItem("http.response.status_code"), StatusCodes.Status422UnprocessableEntity));
    }

    public void Dispose() => _listener.Dispose();

    private static Dictionary<string, object?> Deposit(decimal amount) => new() { ["id"] = AccountId, ["amount"] = amount };

    private static MetricCollector<T> Collect<T>(WebApplication app, string instrument)
        where T : struct =>
        new(app.Services.GetRequiredService<IMeterFactory>(), McpKitTelemetry.MeterName, instrument);

    private static async Task<McpClient> Connect(WebApplication app) =>
        await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

    private async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddMcpKit(options => options.AllowWriteTools = true);

        var app = builder.Build();
        app.MapPost("/accounts/{id}/deposits", (string id, DepositRequest deposit) =>
            {
                _clock.Advance(TimeSpan.FromMilliseconds(250));
                return deposit.Amount > 0
                    ? Results.Ok(new { id, balance = deposit.Amount })
                    : Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity);
            })
            .WithName("Deposit")
            .AsMcpWriteTool();
        app.MapDelete("/accounts/{id}", (string id) => Results.NoContent())
            .WithName("CloseAccount")
            .AsMcpWriteTool()
            .RequireMcpConfirmation();
        app.MapMcpKit();

        await app.StartAsync(Token);
        return app;
    }

    private sealed record DepositRequest(decimal Amount);
}
