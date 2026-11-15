using McpKit.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class ResourceSubscriptionsOverMcpTests
{
    private const string Account = "api://accounts/acc-4471";
    private const string OtherAccount = "api://accounts/acc-9002";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_stateful_host_with_resources_offers_subscriptions()
    {
        await using var app = await StartHost(stateless: false);
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        Assert.True(client.ServerCapabilities.Resources?.Subscribe);
    }

    [Fact]
    public async Task A_stateless_host_does_not_offer_subscriptions()
    {
        await using var app = await StartHost(stateless: true);
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        Assert.NotEqual(true, client.ServerCapabilities.Resources?.Subscribe);
    }

    [Fact]
    public async Task A_subscriber_hears_about_its_uri_and_not_about_others()
    {
        await using var app = await StartHost(stateless: false);
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);
        var updates = new UpdateLog();
        await client.SubscribeToResourceAsync(Account, updates.Record, cancellationToken: Token);

        var notifier = app.Services.GetRequiredService<IMcpResourceNotifier>();
        await notifier.NotifyUpdatedAsync(OtherAccount, Token);
        await notifier.NotifyUpdatedAsync(Account, Token);

        Assert.Equal(Account, await updates.Next().WaitAsync(Wait, Token));
    }

    [Fact]
    public async Task After_unsubscribing_the_client_hears_nothing_more_for_that_uri()
    {
        await using var app = await StartHost(stateless: false);
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);
        var updates = new UpdateLog();
        await client.SubscribeToResourceAsync(Account, updates.Record, cancellationToken: Token);
        await client.SubscribeToResourceAsync(OtherAccount, updates.Record, cancellationToken: Token);
        await client.UnsubscribeFromResourceAsync(Account, cancellationToken: Token);

        var notifier = app.Services.GetRequiredService<IMcpResourceNotifier>();
        await notifier.NotifyUpdatedAsync(Account, Token);
        await notifier.NotifyUpdatedAsync(OtherAccount, Token);

        Assert.Equal(OtherAccount, await updates.Next().WaitAsync(Wait, Token));
    }

    [Fact]
    public async Task Subscribing_to_a_uri_no_resource_matches_is_resource_not_found()
    {
        await using var app = await StartHost(stateless: false);
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var error = await Assert.ThrowsAsync<McpProtocolException>(
            () => client.SubscribeToResourceAsync("api://cards/c-1", cancellationToken: Token));

        Assert.Equal(McpErrorCode.ResourceNotFound, error.ErrorCode);
    }

    [Fact]
    public async Task Notifying_with_no_subscribers_does_nothing()
    {
        await using var app = await StartHost(stateless: true);

        var notifier = app.Services.GetRequiredService<IMcpResourceNotifier>();

        await notifier.NotifyUpdatedAsync(Account, Token);
    }

    private static async Task<WebApplication> StartHost(bool stateless)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMcpKit(options => options.Stateless = stateless);

        var app = builder.Build();
        app.MapGet("/accounts/{id}", (string id) => Results.Ok(new { id, balance = 100.00m }))
            .WithName("GetAccount")
            .AsMcpResource();
        app.MapMcpKit();

        await app.StartAsync(Token);
        return app;
    }

    private sealed class UpdateLog
    {
        private readonly TaskCompletionSource<string> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask Record(ResourceUpdatedNotificationParams update, CancellationToken cancellationToken)
        {
            _first.TrySetResult(update.Uri);
            return ValueTask.CompletedTask;
        }

        public Task<string> Next() => _first.Task;
    }
}
