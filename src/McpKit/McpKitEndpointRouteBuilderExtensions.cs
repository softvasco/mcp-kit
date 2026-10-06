using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace McpKit;

public static class McpKitEndpointRouteBuilderExtensions
{
    /// <summary>Maps the MCP endpoint at the configured path.</summary>
    public static IEndpointConventionBuilder MapMcpKit(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<McpKitOptions>()
            ?? throw new InvalidOperationException("Call AddMcpKit on the service collection before MapMcpKit.");
        return endpoints.MapMcp(options.Path);
    }
}
