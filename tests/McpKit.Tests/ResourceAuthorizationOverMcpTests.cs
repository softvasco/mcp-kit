using System.Text.Json;
using McpKit.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class ResourceAuthorizationOverMcpTests
{
    private const string Account = "api://accounts/acc-1";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_caller_with_the_scope_reads_the_resource()
    {
        var reads = new ReadCount();
        await using var app = await StartHost(reads);
        await using var client = await Connect(app, "accounts:read");

        var result = await client.ReadResourceAsync(Account, cancellationToken: Token);

        var contents = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("acc-1", JsonDocument.Parse(contents.Text).RootElement.GetProperty("id").GetString());
        Assert.Equal(1, reads.Value);
    }

    [Fact]
    public async Task A_caller_without_the_scope_is_refused_before_the_endpoint_runs()
    {
        var reads = new ReadCount();
        await using var app = await StartHost(reads);
        await using var client = await Connect(app, "cards:read");

        var error = await Assert.ThrowsAsync<McpProtocolException>(() => client.ReadResourceAsync(Account, cancellationToken: Token).AsTask());

        Assert.Equal(McpErrorCode.InvalidRequest, error.ErrorCode);
        Assert.Equal(0, reads.Value);
    }

    [Fact]
    public async Task A_prompt_attaching_the_resource_reads_it_as_the_caller()
    {
        var reads = new ReadCount();
        await using var app = await StartHost(reads, ReviewPrompt);
        await using var client = await Connect(app, "cards:read");

        var error = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.GetPromptAsync("review_account", new Dictionary<string, object?> { ["account_id"] = "acc-1" }, cancellationToken: Token).AsTask());

        Assert.Equal(McpErrorCode.InvalidRequest, error.ErrorCode);
        Assert.Equal(0, reads.Value);
    }

    [Fact]
    public async Task A_prompt_attaching_the_resource_works_for_a_caller_with_the_scope()
    {
        var reads = new ReadCount();
        await using var app = await StartHost(reads, ReviewPrompt);
        await using var client = await Connect(app, "accounts:read");

        var result = await client.GetPromptAsync("review_account", new Dictionary<string, object?> { ["account_id"] = "acc-1" }, cancellationToken: Token);

        Assert.IsType<EmbeddedResourceBlock>(result.Messages[^1].Content);
        Assert.Equal(1, reads.Value);
    }

    private static void ReviewPrompt(McpKitOptions options) =>
        options.Prompts.Add("review_account", "Review an account", "Review account {account_id}.", "api://accounts/{account_id}");

    private static async Task<WebApplication> StartHost(ReadCount reads, Action<McpKitOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(ScopesHeader.Name).AddScheme<AuthenticationSchemeOptions, ScopesHeader>(ScopesHeader.Name, null);
        builder.Services.AddAuthorization();
        builder.Services.AddMcpKit(configure ?? (_ => { }));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/accounts/{id}", (string id) =>
            {
                reads.Increment();
                return Results.Ok(new { id, balance = 100.00m });
            })
            .RequireAuthorization(policy => policy.RequireClaim("scope", "accounts:read"))
            .WithName("GetAccount")
            .AsMcpResource();
        app.MapMcpKit().RequireAuthorization();

        await app.StartAsync(Token);
        return app;
    }

    private static async Task<McpClient> Connect(WebApplication app, string scopes)
    {
        var http = app.GetTestClient();
        http.DefaultRequestHeaders.Add(ScopesHeader.Header, scopes);
        return await McpTestClient.ConnectAsync(http, cancellationToken: Token);
    }

    private sealed class ReadCount
    {
        private int _value;

        public int Value => Volatile.Read(ref _value);

        public void Increment() => Interlocked.Increment(ref _value);
    }
}
