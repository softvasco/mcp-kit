using McpKit.OpenApi;

namespace McpKit.Tests.OpenApi;

internal static class SampleDocument
{
    // Path.Join, not Combine: Combine drops the base directory if a later part is rooted
    private static readonly string Accounts = Path.Join(AppContext.BaseDirectory, "OpenApi", "accounts.openapi.json");

    // https://petstore3.swagger.io/api/v3/openapi.json, saved as it was served
    private static readonly string Petstore = Path.Join(AppContext.BaseDirectory, "OpenApi", "petstore.openapi.json");

    public static Task<IReadOnlyList<OpenApiTool>> LoadToolsAsync(CancellationToken cancellationToken) =>
        LoadAsync(Accounts, cancellationToken);

    public static Task<IReadOnlyList<OpenApiTool>> LoadPetstoreAsync(CancellationToken cancellationToken) =>
        LoadAsync(Petstore, cancellationToken);

    private static async Task<IReadOnlyList<OpenApiTool>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        return await OpenApiToolCatalog.LoadAsync(file, cancellationToken);
    }
}
