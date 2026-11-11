using System.Text.Json;
using McpKit.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class PromptAttachmentsOverMcpTests
{
    private const string Review = "Review the account below and list anything unusual.";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_attached_resource_follows_the_text_as_an_embedded_resource()
    {
        await using var app = await StartHost(o => o.Prompts.Add("review_account", "Review an account", Review, "api://accounts/{account_id}"));
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var result = await client.GetPromptAsync("review_account", new Dictionary<string, object?> { ["account_id"] = "acc-1" }, cancellationToken: Token);

        Assert.Equal(2, result.Messages.Count);
        Assert.Equal(Review, Assert.IsType<TextContentBlock>(result.Messages[0].Content).Text);
        var embedded = Assert.IsType<TextResourceContents>(Assert.IsType<EmbeddedResourceBlock>(result.Messages[1].Content).Resource);
        Assert.Equal("api://accounts/acc-1", embedded.Uri);
        Assert.Equal("acc-1", JsonDocument.Parse(embedded.Text).RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_placeholder_only_in_the_uri_is_still_a_required_argument()
    {
        await using var app = await StartHost(o => o.Prompts.Add("review_account", "Review an account", Review, "api://accounts/{account_id}"));
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var prompt = Assert.Single(await client.ListPromptsAsync(cancellationToken: Token));

        Assert.Equal("account_id", Assert.Single(prompt.ProtocolPrompt.Arguments!).Name);
    }

    [Fact]
    public async Task The_attached_read_goes_through_the_hosts_redaction()
    {
        await using var app = await StartHost(o =>
        {
            o.Redaction.KeepLast("iban", 4);
            o.Prompts.Add("review_account", "Review an account", Review, "api://accounts/{account_id}");
        });
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var result = await client.GetPromptAsync("review_account", new Dictionary<string, object?> { ["account_id"] = "acc-1" }, cancellationToken: Token);

        var embedded = Assert.IsType<TextResourceContents>(Assert.IsType<EmbeddedResourceBlock>(result.Messages[1].Content).Resource);
        Assert.DoesNotContain("PT50000201231234567890154", embedded.Text, StringComparison.Ordinal);
        Assert.EndsWith("0154", JsonDocument.Parse(embedded.Text).RootElement.GetProperty("iban").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_with_a_slash_or_space_is_escaped_into_one_route_value()
    {
        await using var app = await StartHost(o => o.Prompts.Add("review_account", "Review an account", Review, "api://accounts/{account_id}"));
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var result = await client.GetPromptAsync("review_account", new Dictionary<string, object?> { ["account_id"] = "acc 1/x" }, cancellationToken: Token);

        var embedded = Assert.IsType<TextResourceContents>(Assert.IsType<EmbeddedResourceBlock>(result.Messages[1].Content).Resource);
        Assert.Equal("acc 1/x", JsonDocument.Parse(embedded.Text).RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task An_endpoint_that_fails_fails_the_prompt_with_its_problem()
    {
        await using var app = await StartHost(o => o.Prompts.Add("review_account", "Review an account", Review, "api://accounts/{account_id}"));
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var error = await Assert.ThrowsAsync<McpProtocolException>(() => client.GetPromptAsync(
            "review_account", new Dictionary<string, object?> { ["account_id"] = "missing" }, cancellationToken: Token).AsTask());

        Assert.Equal(McpErrorCode.ResourceNotFound, error.ErrorCode);
    }

    [Fact]
    public async Task A_uri_that_no_resource_matches_fails_at_startup()
    {
        await using var app = await StartHost(o => o.Prompts.Add("review_transfer", "Review a transfer", Review, "api://transfers/{transfer_id}"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token));

        Assert.Contains("no resource matches", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_attach_that_isnt_a_uri_is_rejected_when_registered()
    {
        Assert.Throws<ArgumentException>(() => new McpPromptTemplates().Add("review_account", "Review an account", Review, "accounts/{account_id}"));
    }

    private static async Task<WebApplication> StartHost(Action<McpKitOptions> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMcpKit(configure);

        var app = builder.Build();
        app.MapGet("/accounts/{id}", (string id) => id == "missing"
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No such account")
                : Results.Ok(new { id, iban = "PT50000201231234567890154" }))
            .WithName("GetAccount")
            .AsMcpResource();
        app.MapMcpKit();

        await app.StartAsync(Token);
        return app;
    }
}
