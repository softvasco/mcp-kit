using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class ToolRateLimitOverMcpTests
{
    private static readonly Dictionary<string, object?> Account = new() { ["id"] = "acc-4471" };

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero));
    private readonly ListAudit _audit = new();
    private int _balanceReads;
    private int _statementReads;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_call_over_the_hosts_limit_is_refused_with_how_long_to_wait_and_the_endpoint_does_not_run()
    {
        await using var app = await StartHost(new ToolRateLimit(2, TimeSpan.FromMinutes(1)));
        await using var client = await Connect(app);

        await client.CallToolAsync("get_balance", Account, cancellationToken: Token);
        await client.CallToolAsync("get_balance", Account, cancellationToken: Token);
        _clock.Advance(TimeSpan.FromSeconds(30.5));
        var refused = await client.CallToolAsync("get_balance", Account, cancellationToken: Token);

        Assert.True(refused.IsError);
        var body = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(refused.Content)).Text).RootElement;
        Assert.Equal("tool.rate_limited", body.GetProperty("code").GetString());
        Assert.Equal(30, body.GetProperty("retry_after_seconds").GetInt32());
        Assert.Equal(2, _balanceReads);
    }

    [Fact]
    public async Task The_limit_on_one_tool_leaves_the_others_alone()
    {
        await using var app = await StartHost(new ToolRateLimit(1, TimeSpan.FromMinutes(1)));
        await using var client = await Connect(app);

        await client.CallToolAsync("get_balance", Account, cancellationToken: Token);
        var second = await client.CallToolAsync("get_balance", Account, cancellationToken: Token);
        var other = await client.CallToolAsync("get_statement", Account, cancellationToken: Token);

        Assert.True(second.IsError);
        Assert.NotEqual(true, other.IsError);
        Assert.Equal(1, _statementReads);
    }

    [Fact]
    public async Task An_endpoint_limit_replaces_the_hosts()
    {
        await using var app = await StartHost(new ToolRateLimit(100, TimeSpan.FromMinutes(1)), statementLimit: 1);
        await using var client = await Connect(app);

        await client.CallToolAsync("get_statement", Account, cancellationToken: Token);
        var second = await client.CallToolAsync("get_statement", Account, cancellationToken: Token);

        Assert.True(second.IsError);
        Assert.Equal(1, _statementReads);
    }

    [Fact]
    public async Task Without_a_limit_every_call_runs()
    {
        await using var app = await StartHost(hostLimit: null);
        await using var client = await Connect(app);

        for (var i = 0; i < 20; i++)
        {
            await client.CallToolAsync("get_balance", Account, cancellationToken: Token);
        }

        Assert.Equal(20, _balanceReads);
    }

    [Fact]
    public async Task A_refused_call_is_audited_as_rate_limited()
    {
        await using var app = await StartHost(new ToolRateLimit(1, TimeSpan.FromMinutes(1)));
        await using var client = await Connect(app);

        await client.CallToolAsync("get_balance", Account, cancellationToken: Token);
        await client.CallToolAsync("get_balance", Account, cancellationToken: Token);

        Assert.Equal(
            [ToolCallOutcome.Succeeded, ToolCallOutcome.RateLimited],
            _audit.Calls.Select(c => c.Outcome));
        Assert.Null(_audit.Calls.Last().StatusCode);
    }

    private async Task<WebApplication> StartHost(ToolRateLimit? hostLimit, int? statementLimit = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddSingleton<IToolCallAudit>(_audit);
        builder.Services.AddMcpKit(options => options.RateLimit = hostLimit);

        var app = builder.Build();
        app.MapGet("/accounts/{id}/balance", (string id) =>
            {
                Interlocked.Increment(ref _balanceReads);
                return Results.Ok(new { id, balance = 25m });
            })
            .WithName("GetBalance")
            .AsMcpTool();
        var statement = app.MapGet("/accounts/{id}/statement", (string id) =>
            {
                Interlocked.Increment(ref _statementReads);
                return Results.Ok(new { id, lines = Array.Empty<string>() });
            })
            .WithName("GetStatement")
            .AsMcpTool();
        if (statementLimit is { } permits)
        {
            statement.WithMcpRateLimit(permits, TimeSpan.FromMinutes(1));
        }

        app.MapMcpKit();

        await app.StartAsync(Token);
        return app;
    }

    private static async Task<McpClient> Connect(WebApplication app)
    {
        var http = app.GetTestClient();
        var options = new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") };
        return await McpClient.CreateAsync(new HttpClientTransport(options, http, ownsHttpClient: true), cancellationToken: Token);
    }

    private sealed class ListAudit : IToolCallAudit
    {
        public ConcurrentQueue<ToolCallRecord> Calls { get; } = new();

        public ValueTask RecordAsync(ToolCallRecord toolCall, CancellationToken cancellationToken)
        {
            Calls.Enqueue(toolCall);
            return ValueTask.CompletedTask;
        }
    }
}
