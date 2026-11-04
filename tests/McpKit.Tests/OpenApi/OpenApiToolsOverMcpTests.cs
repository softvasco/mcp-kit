using System.Net;
using System.Text;
using System.Text.Json;
using McpKit.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests.OpenApi;

public sealed class OpenApiToolsOverMcpTests : IDisposable
{
    private readonly StubUpstream _upstream = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _upstream.Dispose();

    [Fact]
    public async Task Only_read_operations_are_listed_unless_the_host_allows_writes()
    {
        await using var app = await StartHost(allowWrites: false);
        await using var client = await Connect(app);

        var tools = await client.ListToolsAsync(cancellationToken: Token);

        Assert.Equal(["get_account", "list_accounts"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, t => Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint));
    }

    [Fact]
    public async Task Write_operations_appear_when_allowed_and_say_so()
    {
        await using var app = await StartHost(allowWrites: true);
        await using var client = await Connect(app);

        var tools = await client.ListToolsAsync(cancellationToken: Token);

        var deposit = Assert.Single(tools, t => t.Name == "deposit");
        Assert.False(deposit.ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.Equal(6, tools.Count);
    }

    [Fact]
    public async Task A_call_reaches_the_upstream_api_and_its_json_comes_back_redacted()
    {
        _upstream.Respond(HttpStatusCode.OK, """{"id":"acc-1","iban":"PT50000201231234567890154","balance":120.5}""");
        await using var app = await StartHost(allowWrites: false);
        await using var client = await Connect(app);

        var result = await client.CallToolAsync("get_account", new Dictionary<string, object?> { ["id"] = "acc-1" }, cancellationToken: Token);

        Assert.Equal("GET https://bank.example/accounts/acc-1", _upstream.Line);
        Assert.NotEqual(true, result.IsError);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("***0154", structured.GetProperty("iban").GetString());
        Assert.Equal(120.5m, structured.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task An_upstream_error_is_a_tool_error_with_the_api_body()
    {
        _upstream.Respond(HttpStatusCode.NotFound, """{"code":"account.not_found"}""");
        await using var app = await StartHost(allowWrites: false);
        await using var client = await Connect(app);

        var result = await client.CallToolAsync("get_account", new Dictionary<string, object?> { ["id"] = "nope" }, cancellationToken: Token);

        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("account.not_found", JsonElement.Parse(text).GetProperty("code").GetString());
    }

    private async Task<WebApplication> StartHost(bool allowWrites)
    {
        var tools = await SampleDocument.LoadToolsAsync(Token);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddMcpKit(options =>
            {
                options.AllowWriteTools = allowWrites;
                options.Redaction.KeepLast("iban", 4);
            })
            .AddOpenApiTools("bank", tools, http => http.BaseAddress = new Uri("https://bank.example/"));
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => _upstream.Handler()));

        var app = builder.Build();
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

    private sealed class StubUpstream : IDisposable
    {
        private readonly List<StubHandler> _handlers = [];
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "{}";

        public string? Line { get; private set; }

        public void Respond(HttpStatusCode status, string body) => (_status, _body) = (status, body);

        public StubHandler Handler()
        {
            var handler = new StubHandler(this);
            _handlers.Add(handler);
            return handler;
        }

        public void Dispose() => _handlers.ForEach(h => h.Dispose());

        public sealed class StubHandler(StubUpstream upstream) : HttpMessageHandler
        {
            // HttpClient disposes the response it gets back, so the handler hands it over
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(Answer(request));

            private HttpResponseMessage Answer(HttpRequestMessage request)
            {
                upstream.Line = $"{request.Method} {request.RequestUri}";
                return new HttpResponseMessage(upstream._status)
                {
                    Content = new StringContent(upstream._body, Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}
