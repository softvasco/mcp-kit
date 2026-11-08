using McpKit.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace McpKit.Tests.Testing;

public sealed class McpToolSnapshotTests : IAsyncDisposable
{
    private readonly string _folder = Path.Join(Path.GetTempPath(), $"mcp-snapshots-{Guid.NewGuid():N}");
    private WebApplication? _app;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Snapshot => Path.Join(_folder, "host.tools.json");

    private string Received => Path.Join(_folder, "host.tools.received.json");

    [Fact]
    public async Task Tools_are_captured_sorted_by_name_with_their_schemas()
    {
        await using var client = await McpTestClient.ConnectAsync((await StartHost()).GetTestClient(), cancellationToken: Token);

        var snapshot = await McpToolSnapshot.CaptureAsync(client, Token);

        Assert.True(snapshot.IndexOf("\"get_account\"", StringComparison.Ordinal) < snapshot.IndexOf("\"list_accounts\"", StringComparison.Ordinal));
        Assert.Contains("\"inputSchema\"", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_snapshot_fails_and_leaves_the_received_file_to_review()
    {
        var error = Assert.Throws<McpToolSnapshotException>(() => McpToolSnapshot.Match("[]\n", Snapshot));

        Assert.Contains("No tool snapshot", error.Message, StringComparison.Ordinal);
        Assert.Equal("[]\n", File.ReadAllText(Received));
    }

    [Fact]
    public void A_changed_tool_list_fails_and_a_matching_one_clears_the_received_file()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Snapshot, "[\r\n]\r\n");

        Assert.Throws<McpToolSnapshotException>(() => McpToolSnapshot.Match("[ {} ]\n", Snapshot));
        McpToolSnapshot.Match("[\n]\n", Snapshot);

        Assert.False(File.Exists(Received));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMcpKit();

        _app = builder.Build();
        _app.MapGet("/accounts", () => Results.Ok()).WithName("ListAccounts").AsMcpTool();
        _app.MapGet("/accounts/{id}", (string id) => Results.Ok(new { id })).WithName("GetAccount").AsMcpTool();
        _app.MapMcpKit();

        await _app.StartAsync(Token);
        return _app;
    }
}
