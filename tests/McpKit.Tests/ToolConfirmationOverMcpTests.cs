using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class ToolConfirmationOverMcpTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly Deposits _deposits = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_call_runs_nothing_and_returns_a_token()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);

        var result = await client.CallToolAsync("deposit", Args(25m), cancellationToken: Token);

        Assert.NotEqual(true, result.IsError);
        var pending = Json(result);
        Assert.Equal("confirmation_required", pending.GetProperty("status").GetString());
        Assert.Equal(25m, pending.GetProperty("arguments").GetProperty("amount").GetDecimal());
        Assert.False(string.IsNullOrEmpty(pending.GetProperty("confirmation_token").GetString()));
        Assert.Equal(0, _deposits.Count);
    }

    [Fact]
    public async Task Calling_again_with_the_token_runs_the_endpoint_once()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);
        var token = await PreviewAsync(client, 25m);

        var result = await client.CallToolAsync("deposit", Args(25m, token), cancellationToken: Token);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(25m, Json(result).GetProperty("balance").GetDecimal());
        Assert.Equal(1, _deposits.Count);
    }

    [Fact]
    public async Task A_token_for_one_amount_does_not_confirm_another()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);
        var token = await PreviewAsync(client, 25m);

        var result = await client.CallToolAsync("deposit", Args(2500m, token), cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal("tool.confirmation_invalid", Json(result).GetProperty("code").GetString());
        Assert.Equal(0, _deposits.Count);
    }

    [Fact]
    public async Task An_expired_token_runs_nothing()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);
        var token = await PreviewAsync(client, 25m);

        _clock.Advance(TimeSpan.FromMinutes(2));
        var result = await client.CallToolAsync("deposit", Args(25m, token), cancellationToken: Token);

        Assert.Equal("tool.confirmation_expired", Json(result).GetProperty("code").GetString());
        Assert.Equal(0, _deposits.Count);
    }

    [Fact]
    public async Task The_schema_tells_the_model_about_the_token()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);

        var tool = Assert.Single(await client.ListToolsAsync(cancellationToken: Token));

        var properties = tool.JsonSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("confirmation_token", out _));
        Assert.DoesNotContain("confirmation_token", tool.JsonSchema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
    }

    private async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddMcpKit(options => options.AllowWriteTools = true);

        var app = builder.Build();
        app.MapPost("/accounts/{id}/deposits", (string id, DepositRequest deposit) =>
            {
                _deposits.Count++;
                return Results.Ok(new { id, balance = deposit.Amount });
            })
            .WithName("Deposit")
            .AsMcpWriteTool()
            .RequireMcpConfirmation(TimeSpan.FromMinutes(1));
        app.MapMcpKit();

        await app.StartAsync(Token);
        return app;
    }

    private static async Task<string> PreviewAsync(McpClient client, decimal amount) =>
        Json(await client.CallToolAsync("deposit", Args(amount), cancellationToken: Token))
            .GetProperty("confirmation_token").GetString()!;

    private static Dictionary<string, object?> Args(decimal amount, string? token = null)
    {
        var args = new Dictionary<string, object?> { ["id"] = "acc-1", ["amount"] = amount };
        if (token is not null)
        {
            args["confirmation_token"] = token;
        }

        return args;
    }

    private static JsonElement Json(CallToolResult result) =>
        JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text).RootElement;

    private static async Task<McpClient> Connect(WebApplication app)
    {
        var http = app.GetTestClient();
        var options = new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") };
        return await McpClient.CreateAsync(new HttpClientTransport(options, http, ownsHttpClient: true), cancellationToken: Token);
    }

    private sealed class Deposits
    {
        public int Count { get; set; }
    }

    private sealed record DepositRequest(decimal Amount);
}
