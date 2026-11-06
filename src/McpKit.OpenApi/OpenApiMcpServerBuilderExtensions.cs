using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace McpKit.OpenApi;

public static class OpenApiMcpServerBuilderExtensions
{
    /// <summary>Adds tools for the operations of an OpenAPI document. Configure the client with the API's base address.</summary>
    public static IMcpServerBuilder AddOpenApiTools(
        this IMcpServerBuilder builder, string name, IReadOnlyList<OpenApiTool> tools, Action<HttpClient> configureClient) =>
        builder.AddOpenApiTools(name, tools, configureClient, _ => { });

    /// <summary>Adds only the operations picked by the selection; an unknown name fails here.</summary>
    public static IMcpServerBuilder AddOpenApiTools(
        this IMcpServerBuilder builder,
        string name,
        IReadOnlyList<OpenApiTool> tools,
        Action<HttpClient> configureClient,
        Action<OpenApiToolSelection> select)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(configureClient);
        ArgumentNullException.ThrowIfNull(select);

        var selection = new OpenApiToolSelection();
        select(selection);
        var served = selection.Apply(tools);

        var clientName = ClientName(name);
        builder.Services.AddHttpClient(clientName, configureClient);

        // same rule as endpoint tools: writes stay out unless the host allows them
        builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>>(provider =>
            new ConfigureOptions<McpServerOptions>(server =>
            {
                var allowWrites = provider.GetRequiredService<McpKitOptions>().AllowWriteTools;
                server.ToolCollection ??= [];
                foreach (var tool in served.Where(t => allowWrites || !t.ChangesState))
                {
                    server.ToolCollection.Add(new OpenApiMcpServerTool(tool, clientName));
                }
            }));

        return builder;
    }

    /// <summary>Sets the Authorization header of each call to the named API from the caller.</summary>
    public static IMcpServerBuilder WithUpstreamCredentials(
        this IMcpServerBuilder builder, string name, UpstreamCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(credentials);

        builder.Services.AddHttpClient(ClientName(name))
            .AddHttpMessageHandler(services => new UpstreamCredentialsHandler(credentials, services));

        return builder;
    }

    private static string ClientName(string name) => $"McpKit.OpenApi:{name}";
}
