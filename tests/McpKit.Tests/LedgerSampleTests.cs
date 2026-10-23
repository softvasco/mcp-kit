using System.Text.Json;
using McpKit.LedgerSample;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public sealed class LedgerSampleTests : IAsyncDisposable
{
    private readonly WebApplicationFactory<AccountReadModel> _host = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_sees_two_read_only_tools_described_by_the_endpoint_summaries()
    {
        await using var client = await Connect();

        var tools = await client.ListToolsAsync(cancellationToken: Token);

        Assert.Equal(["get_account", "get_statement"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Balance, currency and status of one account", Assert.Single(tools, t => t.Name == "get_account").Description);
        Assert.All(tools, t => Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint));
    }

    [Fact]
    public async Task Get_account_returns_the_balance_as_an_exact_decimal()
    {
        await using var client = await Connect();

        var account = Json(await client.CallToolAsync("get_account", Id(AccountReadModel.Lisbon), cancellationToken: Token));

        Assert.Equal(59.50m, account.GetProperty("balance").GetDecimal());
        Assert.Equal("EUR", account.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task An_unknown_account_comes_back_as_an_error_with_a_code_the_agent_can_branch_on()
    {
        await using var client = await Connect();

        var result = await client.CallToolAsync("get_account", Id(Guid.CreateVersion7()), cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal("account.not_found", Json(result).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_statement_is_newest_first_and_take_limits_it()
    {
        await using var client = await Connect();
        var arguments = Id(AccountReadModel.Lisbon);
        arguments["take"] = 1;

        var lines = Json(await client.CallToolAsync("get_statement", arguments, cancellationToken: Token));

        var line = Assert.Single(lines.EnumerateArray());
        Assert.Equal("Card payment", line.GetProperty("description").GetString());
        Assert.Equal(-40.50m, line.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task A_take_out_of_range_is_refused_by_the_endpoint_not_clamped()
    {
        await using var client = await Connect();
        var arguments = Id(AccountReadModel.Lisbon);
        arguments["take"] = 0;

        var result = await client.CallToolAsync("get_statement", arguments, cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal("statement.take_out_of_range", Json(result).GetProperty("code").GetString());
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private static Dictionary<string, object?> Id(Guid id) => new() { ["id"] = id.ToString() };

    private static JsonElement Json(CallToolResult result) =>
        JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text).RootElement;

    private async Task<McpClient> Connect()
    {
        var http = _host.CreateClient();
        var options = new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") };
        return await McpClient.CreateAsync(new HttpClientTransport(options, http, ownsHttpClient: true), cancellationToken: Token);
    }
}
