using McpKit.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class PromptTemplatesOverMcpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_registered_template_is_listed_with_its_placeholders_as_required_arguments()
    {
        await using var app = await StartHost(o => o.Prompts.Add("review_account", "Review an account", "Review {account_id} for {period}."));
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var prompt = Assert.Single(await client.ListPromptsAsync(cancellationToken: Token));

        Assert.Equal("review_account", prompt.Name);
        Assert.Equal("Review an account", prompt.Description);
        Assert.Equal(["account_id", "period"], prompt.ProtocolPrompt.Arguments!.Select(a => a.Name));
        Assert.All(prompt.ProtocolPrompt.Arguments!, a => Assert.True(a.Required));
    }

    [Fact]
    public async Task Getting_a_prompt_returns_one_user_message_with_the_arguments_filled_in()
    {
        await using var app = await StartHost(o => o.Prompts.Add("review_account", "Review an account", "Review {account_id} for {period}."));
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var result = await client.GetPromptAsync(
            "review_account", new Dictionary<string, object?> { ["account_id"] = "acc-1", ["period"] = "October" }, cancellationToken: Token);

        var message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        Assert.Equal("Review acc-1 for October.", Assert.IsType<TextContentBlock>(message.Content).Text);
    }

    [Fact]
    public async Task A_missing_argument_is_an_invalid_params_error_that_names_it()
    {
        await using var app = await StartHost(o => o.Prompts.Add("review_account", "Review an account", "Review {account_id} for {period}."));
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        var error = await Assert.ThrowsAsync<McpProtocolException>(() => client.GetPromptAsync(
            "review_account", new Dictionary<string, object?> { ["account_id"] = "acc-1" }, cancellationToken: Token).AsTask());

        Assert.Equal(McpErrorCode.InvalidParams, error.ErrorCode);
        Assert.Contains("needs period", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_without_prompts_does_not_advertise_them()
    {
        await using var app = await StartHost(_ => { });
        await using var client = await McpTestClient.ConnectAsync(app.GetTestClient(), cancellationToken: Token);

        Assert.Null(client.ServerCapabilities.Prompts);
    }

    private static async Task<WebApplication> StartHost(Action<McpKitOptions> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMcpKit(configure);

        var app = builder.Build();
        app.MapMcpKit();

        await app.StartAsync(Token);
        return app;
    }
}
