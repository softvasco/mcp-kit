using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace McpKit;

public static class McpKitServiceCollectionExtensions
{
    /// <summary>Registers an MCP server over HTTP. Add tools on the returned builder.</summary>
    public static IMcpServerBuilder AddMcpKit(this IServiceCollection services, Action<McpKitOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new McpKitOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<EndpointToolCatalog>();

        // runs when the server is first built, after the host has mapped its endpoints
        services.AddSingleton<IConfigureOptions<McpServerOptions>>(provider =>
            new ConfigureOptions<McpServerOptions>(server =>
            {
                server.ToolCollection ??= [];
                var tools = provider.GetRequiredService<EndpointToolCatalog>().Discover().Where(t => options.AllowWriteTools || !t.ChangesState);
                foreach (var tool in tools)
                {
                    server.ToolCollection.Add(new EndpointMcpServerTool(tool));
                }
            }));

        // tools carry their endpoint's metadata, so the SDK's filters hide what the caller's policy refuses
        return services.AddMcpServer()
            .WithHttpTransport(transport => transport.Stateless = options.Stateless)
            .AddAuthorizationFilters();
    }
}
