using McpKit.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace McpKit.Tests.Testing;

public sealed class McpTestClientTests : IAsyncDisposable
{
    private static readonly string[] AccountIds = ["acc-1", "acc-2"];

    private WebApplication? _app;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_object_result_comes_back_as_its_json()
    {
        await using var client = await McpTestClient.ConnectAsync((await StartHost()).GetTestClient(), cancellationToken: Token);

        var account = await client.CallToolForJsonAsync("get_account", Args(("id", "acc-1")), Token);

        Assert.Equal("acc-1", account.GetProperty("id").GetString());
        Assert.Equal(120.50m, account.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task An_array_result_is_read_from_the_text()
    {
        await using var client = await McpTestClient.ConnectAsync((await StartHost()).GetTestClient(), cancellationToken: Token);

        var ids = await client.CallToolForJsonAsync("list_accounts", cancellationToken: Token);

        Assert.Equal(["acc-1", "acc-2"], ids.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task A_tool_error_throws_with_what_the_model_would_have_read()
    {
        await using var client = await McpTestClient.ConnectAsync((await StartHost()).GetTestClient(), cancellationToken: Token);

        var error = await Assert.ThrowsAsync<McpToolCallException>(
            () => client.CallToolForJsonAsync("get_account", Args(("id", "missing")), Token));

        Assert.Equal("get_account", error.ToolName);
        Assert.Contains("account.not_found", error.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_client_without_a_base_address_is_refused()
    {
        using var http = new HttpClient();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => McpTestClient.ConnectAsync(http, cancellationToken: Token));

        Assert.Equal("http", error.ParamName);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value);

    private async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMcpKit();

        _app = builder.Build();
        _app.MapGet("/accounts/{id}", (string id) => id == "missing"
                ? Results.Problem(statusCode: 404, title: "No such account.", extensions: new Dictionary<string, object?> { ["code"] = "account.not_found" })
                : Results.Ok(new { id, balance = 120.50m }))
            .WithName("GetAccount")
            .AsMcpTool();
        _app.MapGet("/accounts", () => Results.Ok(AccountIds)).WithName("ListAccounts").AsMcpTool();
        _app.MapMcpKit();

        await _app.StartAsync(Token);
        return _app;
    }
}
