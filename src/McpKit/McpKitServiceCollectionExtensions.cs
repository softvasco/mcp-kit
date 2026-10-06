using Microsoft.Extensions.DependencyInjection;

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

        return services.AddMcpServer().WithHttpTransport(transport => transport.Stateless = options.Stateless);
    }
}
