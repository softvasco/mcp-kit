using McpKit.OpenApi;

namespace McpKit.OpenApiGateway;

// serves the operations of one OpenAPI document as MCP tools, so any HTTP API can sit behind it
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var section = builder.Configuration.GetSection(UpstreamOptions.Section);
        var upstream = section.Get<UpstreamOptions>() ?? new UpstreamOptions();
        upstream.Validate();

        // an absolute path in config is used as it is, a relative one is taken from the content root
        var path = Path.GetFullPath(upstream.Document!, builder.Environment.ContentRootPath);
        IReadOnlyList<OpenApiTool> tools;
        await using (var document = File.OpenRead(path))
        {
            tools = await OpenApiToolCatalog.LoadAsync(document);
        }

        builder.Services.AddHealthChecks();
        builder.Services.AddMcpKit(mcp => mcp.AllowWriteTools = upstream.AllowWriteTools)
            .AddOpenApiTools(
                "upstream",
                tools,
                http => http.BaseAddress = upstream.ClientBaseAddress(),
                select => section.GetSection("Tools").Bind(select));

        var app = builder.Build();
        app.MapHealthChecks("/healthz");
        app.MapMcpKit();
        await app.RunAsync();
    }
}
