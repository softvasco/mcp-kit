using System.Net;
using McpKit.OpenApiGateway;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace McpKit.Tests;

public sealed class OpenApiGatewaySampleTests : IAsyncDisposable
{
    private static readonly string Petstore = Path.Join(AppContext.BaseDirectory, "OpenApi", "petstore.openapi.json");

    private readonly Upstream _upstream = new();
    private WebApplicationFactory<UpstreamOptions>? _host;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_the_read_only_operations_are_served_unless_writes_are_allowed()
    {
        await using var client = await Connect();

        var tools = await client.ListToolsAsync(cancellationToken: Token);

        Assert.Equal(
            [
                "find_pets_by_status", "find_pets_by_tags", "get_inventory", "get_order_by_id", "get_pet_by_id",
                "get_user_by_name", "login_user", "logout_user",
            ],
            tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_base_address_without_a_trailing_slash_keeps_its_last_segment()
    {
        await using var client = await Connect();

        await client.CallToolAsync("get_pet_by_id", new Dictionary<string, object?> { ["petId"] = 10 }, cancellationToken: Token);

        Assert.Equal("GET https://petstore.example/api/v3/pet/10", _upstream.Line);
    }

    [Fact]
    public async Task The_tool_selection_comes_from_config()
    {
        await using var client = await Connect(("Upstream:Tools:Include:0", "get_pet_by_id"), ("Upstream:Tools:Rename:get_pet_by_id", "pet"));

        var tool = Assert.Single(await client.ListToolsAsync(cancellationToken: Token));

        Assert.Equal("pet", tool.Name);
    }

    [Fact]
    public async Task A_missing_base_address_stops_the_host_with_what_to_set()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Connect(("Upstream:BaseAddress", "")));

        Assert.Contains("Upstream:BaseAddress", error.Message, StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        _upstream.Dispose();
    }

    private async Task<McpClient> Connect(params (string Key, string Value)[] settings)
    {
        _host = new WebApplicationFactory<UpstreamOptions>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Upstream:Document", Petstore);
            web.UseSetting("Upstream:BaseAddress", "https://petstore.example/api/v3");
            foreach (var (key, value) in settings)
            {
                web.UseSetting(key, value);
            }

            web.ConfigureTestServices(services =>
                services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => _upstream)));
        });

        var http = _host.CreateClient();
        var options = new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") };
        return await McpClient.CreateAsync(new HttpClientTransport(options, http, ownsHttpClient: true), cancellationToken: Token);
    }

    private sealed class Upstream : HttpMessageHandler
    {
        public string? Line { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Line = $"{request.Method} {request.RequestUri!.AbsoluteUri}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
