using McpKit.LedgerSample;
using McpKit.OpenApiGateway;
using McpKit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace McpKit.Tests;

// a change to any schema below is a change for every agent already calling these tools, so it has to be seen in review
public class ToolSchemaSnapshotTests
{
    private static readonly string Petstore = Path.Join(AppContext.BaseDirectory, "OpenApi", "petstore.openapi.json");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ledger_sample_tools()
    {
        await using var host = new WebApplicationFactory<AccountReadModel>();
        await using var client = await McpTestClient.ConnectAsync(host.CreateClient(), cancellationToken: Token);

        await McpToolSnapshot.MatchAsync(client, "ledger-sample", Token);
    }

    [Fact]
    public async Task Petstore_tools_generated_from_openapi()
    {
        await using var host = new WebApplicationFactory<UpstreamOptions>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Upstream:Document", Petstore);
            web.UseSetting("Upstream:BaseAddress", "https://petstore.example/api/v3");
            web.UseSetting("Upstream:AllowWriteTools", "true");
        });
        await using var client = await McpTestClient.ConnectAsync(host.CreateClient(), cancellationToken: Token);

        await McpToolSnapshot.MatchAsync(client, "petstore", Token);
    }
}
