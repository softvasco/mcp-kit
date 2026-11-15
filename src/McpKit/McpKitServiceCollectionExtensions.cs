using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
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
        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ToolConfirmations>();
        services.AddSingleton<ToolRateLimiter>();
        services.TryAddSingleton<IToolCallAudit, LoggerToolCallAudit>();
        services.AddMetrics();
        services.AddSingleton<ToolCallTelemetry>();
        services.AddSingleton<ResourceSubscriptions>();
        services.AddSingleton<IMcpResourceNotifier>(provider => provider.GetRequiredService<ResourceSubscriptions>());

        // runs when the server is first built, after the host has mapped its endpoints
        services.AddSingleton<IConfigureOptions<McpServerOptions>>(provider =>
            new ConfigureOptions<McpServerOptions>(server =>
            {
                server.ToolCollection ??= [];
                var catalog = provider.GetRequiredService<EndpointToolCatalog>();
                var tools = catalog.Discover().Where(t => options.AllowWriteTools || !t.ChangesState);
                foreach (var tool in tools)
                {
                    server.ToolCollection.Add(new EndpointMcpServerTool(tool));
                }

                // only a host with resources should tell clients it has any
                var resources = catalog.DiscoverResources(options.ResourceScheme);
                if (resources.Count > 0)
                {
                    server.ResourceCollection ??= [];
                    foreach (var resource in resources)
                    {
                        server.ResourceCollection.Add(new EndpointMcpServerResource(resource));
                    }

                    // a stateless server has no session to send a notification on later
                    if (!options.Stateless)
                    {
                        AllowSubscriptions(server, resources, provider.GetRequiredService<ResourceSubscriptions>());
                    }
                }

                if (options.Prompts.Count > 0)
                {
                    server.PromptCollection ??= [];
                    foreach (var prompt in options.Prompts)
                    {
                        server.PromptCollection.Add(new TemplateMcpServerPrompt(prompt, resources));
                    }
                }
            }));

        // tools carry their endpoint's metadata, so the SDK's filters hide what the caller's policy refuses
        return services.AddMcpServer()
            .WithHttpTransport(transport => transport.Stateless = options.Stateless)
            .AddAuthorizationFilters();
    }

    private static void AllowSubscriptions(
        McpServerOptions server, IReadOnlyList<EndpointResource> resources, ResourceSubscriptions subscriptions)
    {
        server.Capabilities ??= new ServerCapabilities();
        server.Capabilities.Resources ??= new ResourcesCapability();
        server.Capabilities.Resources.Subscribe = true;

        server.Handlers.SubscribeToResourcesHandler = (request, _) =>
        {
            var uri = KnownUri(request.Params?.Uri, resources);
            subscriptions.Subscribe(request.Server, uri);
            return ValueTask.FromResult(new EmptyResult());
        };
        server.Handlers.UnsubscribeFromResourcesHandler = (request, _) =>
        {
            var uri = request.Params?.Uri ?? throw new McpProtocolException("The request has no URI.", McpErrorCode.InvalidParams);
            subscriptions.Unsubscribe(request.Server, uri);
            return ValueTask.FromResult(new EmptyResult());
        };
    }

    private static string KnownUri(string? uri, IReadOnlyList<EndpointResource> resources)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new McpProtocolException("The request has no URI.", McpErrorCode.InvalidParams);
        }

        return resources.Any(r => r.Uri.Match(uri) is not null)
            ? uri
            : throw new McpProtocolException($"No resource has the URI '{uri}'.", McpErrorCode.ResourceNotFound);
    }
}
