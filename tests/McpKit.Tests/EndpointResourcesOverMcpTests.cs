using System.Text.Json;
using McpKit.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class EndpointResourcesOverMcpTests
{
    private static readonly string[] Lines = ["opening", "deposit 100.00", "withdrawal 30.00", "fee 1.50", "deposit 20.00"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_get_endpoint_marked_as_a_resource_is_listed_as_a_template_with_its_query()
    {
        await using var app = await StartHost();
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var template = Assert.Single(await client.ListResourceTemplatesAsync(cancellationToken: Token), t => t.Name == "get_statement");

        Assert.Equal("api://accounts/{id}/statement{?page,pageSize}", template.UriTemplate);
        Assert.Equal("application/json", template.ProtocolResourceTemplate.MimeType);
    }

    [Fact]
    public async Task Reading_a_uri_runs_the_endpoint_with_the_route_value_and_the_page()
    {
        await using var app = await StartHost();
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var result = await client.ReadResourceAsync("api://accounts/acc-4471/statement?page=2&pageSize=2", cancellationToken: Token);

        var contents = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("api://accounts/acc-4471/statement?page=2&pageSize=2", contents.Uri);
        var page = JsonDocument.Parse(contents.Text).RootElement;
        Assert.Equal("acc-4471", page.GetProperty("id").GetString());
        Assert.Equal(["withdrawal 30.00", "fee 1.50"], page.GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal(5, page.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Without_a_page_the_endpoint_defaults_apply()
    {
        await using var app = await StartHost();
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var result = await client.ReadResourceAsync("api://accounts/acc-4471/statement", cancellationToken: Token);

        var page = JsonDocument.Parse(Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text).RootElement;
        Assert.Equal(["opening", "deposit 100.00"], page.GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task A_404_from_the_endpoint_is_resource_not_found_with_the_problem_text()
    {
        await using var app = await StartHost();
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var error = await Assert.ThrowsAsync<McpProtocolException>(
            () => client.ReadResourceAsync("api://accounts/missing/statement", cancellationToken: Token).AsTask());

        Assert.Equal(McpErrorCode.ResourceNotFound, error.ErrorCode);
        Assert.Contains("account.not_found", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_parameter_the_endpoint_does_not_take_is_refused_before_it_runs()
    {
        await using var app = await StartHost();
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var error = await Assert.ThrowsAsync<McpProtocolException>(
            () => client.ReadResourceAsync("api://accounts/acc-4471/statement?sort=desc", cancellationToken: Token).AsTask());

        Assert.Equal(McpErrorCode.InvalidParams, error.ErrorCode);
        Assert.Contains("sort", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_is_not_a_number_is_invalid_params()
    {
        await using var app = await StartHost();
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var error = await Assert.ThrowsAsync<McpProtocolException>(
            () => client.ReadResourceAsync("api://accounts/acc-4471/statement?page=two", cancellationToken: Token).AsTask());

        Assert.Equal(McpErrorCode.InvalidParams, error.ErrorCode);
    }

    [Fact]
    public async Task Resource_contents_are_redacted_like_tool_output()
    {
        await using var app = await StartHost(options => options.Redaction.KeepLast("iban", 4));
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var result = await client.ReadResourceAsync("api://accounts/acc-4471", cancellationToken: Token);

        var account = JsonDocument.Parse(Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text).RootElement;
        Assert.Equal("***0154", account.GetProperty("iban").GetString());
    }

    [Fact]
    public async Task A_host_without_resources_does_not_offer_any()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMcpKit();
        await using var app = builder.Build();
        app.MapGet("/time", () => Results.Ok(new { now = "12:00" })).WithName("GetTime").AsMcpTool();
        app.MapMcpKit();
        await app.StartAsync(Token);
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        Assert.Null(client.ServerCapabilities.Resources);
    }

    private static async Task<WebApplication> StartHost(Action<McpKitOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMcpKit(configure);

        var app = builder.Build();
        app.MapGet("/accounts/{id}/statement", (string id, int page = 1, int pageSize = 2) => id == "missing"
                ? Results.Problem(statusCode: 404, title: "No such account.", extensions: new Dictionary<string, object?> { ["code"] = "account.not_found" })
                : Results.Ok(new { id, page, lines = Lines.Skip((page - 1) * pageSize).Take(pageSize), total = Lines.Length }))
            .WithName("GetStatement")
            .WithSummary("Statement lines for an account, one page at a time.")
            .AsMcpResource();
        app.MapGet("/accounts/{id}", (string id) => Results.Ok(new { id, iban = "PT50000201231234567890154" }))
            .WithName("GetAccount")
            .AsMcpResource();
        app.MapMcpKit();

        await app.StartAsync(Token);
        return app;
    }
}
