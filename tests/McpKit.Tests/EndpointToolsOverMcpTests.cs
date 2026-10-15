using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class EndpointToolsOverMcpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Marked_endpoints_are_listed_with_their_route_parameters()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);

        var tools = await client.ListToolsAsync(cancellationToken: Token);

        var tool = Assert.Single(tools, t => t.Name == "deposit");
        Assert.Equal("Deposit money into an account", tool.Description);
        Assert.Equal("id", Assert.Single(tool.JsonSchema.GetProperty("required").EnumerateArray()).GetString());
        Assert.DoesNotContain(tools, t => t.Name == "close_account");
    }

    [Fact]
    public async Task A_tool_call_runs_the_endpoint()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);

        var result = await client.CallToolAsync(
            "deposit",
            new Dictionary<string, object?> { ["id"] = "acc-1", ["amount"] = 25m },
            cancellationToken: Token);

        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal(25m, JsonDocument.Parse(text.Text).RootElement.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task A_problem_from_the_endpoint_is_a_tool_error_with_the_same_body()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);

        var result = await client.CallToolAsync(
            "deposit",
            new Dictionary<string, object?> { ["id"] = "acc-1", ["amount"] = -5m },
            cancellationToken: Token);

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("amount.not_positive", JsonDocument.Parse(text.Text).RootElement.GetProperty("code").GetString());
    }

    private static async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMcpKit();

        var app = builder.Build();
        app.MapPost("/accounts/{id}/deposits", (string id, DepositRequest deposit) => deposit.Amount > 0
                ? Results.Ok(new { id, balance = deposit.Amount })
                : Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, extensions: new Dictionary<string, object?> { ["code"] = "amount.not_positive" }))
            .WithName("Deposit")
            .WithSummary("Deposit money into an account")
            .AsMcpTool();
        app.MapDelete("/accounts/{id}", (string id) => Results.NoContent()).WithName("CloseAccount");
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

    private sealed record DepositRequest(decimal Amount);
}
