using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class SampleHostTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_host_lists_its_tools_over_http()
    {
        await using var factory = new WebApplicationFactory<Program>();
        await using var client = await Connect(factory);

        var tools = await client.ListToolsAsync(cancellationToken: Token);

        var tool = Assert.Single(tools);
        Assert.Equal("utc_now", tool.Name);
        Assert.Equal("The server's current time in UTC, ISO 8601.", tool.Description);
    }

    [Fact]
    public async Task A_tool_call_runs_against_the_host_services()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureServices(services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(Noon))));
        await using var client = await Connect(factory);

        var result = await client.CallToolAsync("utc_now", cancellationToken: Token);

        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("2026-10-06T12:00:00.0000000+00:00", text.Text);
    }

    [Fact]
    public async Task The_endpoint_path_is_configurable()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureServices(services => services.AddSingleton(new McpKitOptions { Path = "/agents" })));
        await using var client = await Connect(factory, "/agents");

        Assert.NotEmpty(await client.ListToolsAsync(cancellationToken: Token));
    }

    private static async Task<McpClient> Connect(WebApplicationFactory<Program> factory, string path = "/mcp")
    {
        var http = factory.CreateClient();
        var options = new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, path) };
        return await McpClient.CreateAsync(new HttpClientTransport(options, http), cancellationToken: Token);
    }
}
