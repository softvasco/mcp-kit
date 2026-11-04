using McpKit.OpenApi;

namespace McpKit.Tests.OpenApi;

internal static class SampleDocument
{
    // Path.Join, not Combine: Combine drops the base directory if a later part is rooted
    private static readonly string Accounts = Path.Join(AppContext.BaseDirectory, "OpenApi", "accounts.openapi.json");

    public static async Task<IReadOnlyList<OpenApiTool>> LoadToolsAsync(CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(Accounts);
        return await OpenApiToolCatalog.LoadAsync(file, cancellationToken);
    }
}
