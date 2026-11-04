using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace McpKit.OpenApi;

public static class OpenApiMcpServerBuilderExtensions
{
    /// <summary>Adds tools for the operations of an OpenAPI document. Configure the client with the API's base address.</summary>
    public static IMcpServerBuilder AddOpenApiTools(
        this IMcpServerBuilder builder, string name, IReadOnlyList<OpenApiTool> tools, Action<HttpClient> configureClient)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(configureClient);

        var clientName = $"McpKit.OpenApi:{name}";
        builder.Services.AddHttpClient(clientName, configureClient);

        // same rule as endpoint tools: writes stay out unless the host allows them
        builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>>(provider =>
            new ConfigureOptions<McpServerOptions>(server =>
            {
                var allowWrites = provider.GetRequiredService<McpKitOptions>().AllowWriteTools;
                server.ToolCollection ??= [];
                foreach (var tool in tools.Where(t => allowWrites || !t.ChangesState))
                {
                    server.ToolCollection.Add(new OpenApiMcpServerTool(tool, clientName));
                }
            }));

        return builder;
    }
}
