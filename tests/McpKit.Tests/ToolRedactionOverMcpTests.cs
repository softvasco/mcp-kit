using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class ToolRedactionOverMcpTests
{
    private const string Iban = "PT50000201231234567890154";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Host_rules_mask_the_text_and_the_structured_content()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);

        var result = await client.CallToolAsync("get_account", new Dictionary<string, object?> { ["id"] = "acc-1" }, cancellationToken: Token);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain(Iban, text);
        Assert.DoesNotContain("joao@example.com", text);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("***0154", structured.GetProperty("iban").GetString());
        Assert.Equal("***", structured.GetProperty("holder").GetProperty("email").GetString());
        Assert.Equal("João Silva", structured.GetProperty("holder").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_tool_adds_its_own_rules_to_the_host_ones()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app);

        var result = await client.CallToolAsync("get_payee", new Dictionary<string, object?> { ["id"] = "p-1" }, cancellationToken: Token);

        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("***", structured.GetProperty("name").GetString());
        Assert.Equal("***", structured.GetProperty("iban").GetString());
    }

    [Fact]
    public async Task The_endpoint_itself_still_returns_the_full_values()
    {
        await using var app = await StartHost();

        var body = await app.GetTestClient().GetStringAsync(new Uri("/accounts/acc-1", UriKind.Relative), Token);

        Assert.Contains(Iban, body);
    }

    private static async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMcpKit(options => options.Redaction.KeepLast("iban", 4).Mask("email"));

        var app = builder.Build();
        app.MapGet("/accounts/{id}", (string id) => Results.Ok(new { id, iban = Iban, holder = new { name = "João Silva", email = "joao@example.com" } }))
            .WithName("GetAccount")
            .AsMcpTool();
        app.MapGet("/payees/{id}", (string id) => Results.Ok(new { id, name = "Maria Costa", iban = Iban }))
            .WithName("GetPayee")
            .AsMcpTool()
            .WithMcpRedaction(r => r.Mask("name").Mask("iban"));
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
}
