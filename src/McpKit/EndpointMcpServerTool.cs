using System.Security.Claims;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpKit;

/// <summary>An MCP tool backed by an endpoint; a call runs the endpoint.</summary>
internal sealed class EndpointMcpServerTool(EndpointTool tool) : McpServerTool
{
    public override Tool ProtocolTool { get; } = new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = InputSchema(tool),
    };

    public override IReadOnlyList<object> Metadata => [.. tool.Endpoint.Metadata];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var services = request.Services ?? throw new InvalidOperationException("The MCP request has no service provider.");
        var arguments = request.Params?.Arguments ?? new Dictionary<string, JsonElement>();
        var user = request.User ?? new ClaimsPrincipal(new ClaimsIdentity());

        var response = await EndpointToolInvoker.InvokeAsync(tool, arguments.AsReadOnly(), services, user, cancellationToken);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = response.Body }],
            IsError = !response.IsSuccess,
        };
    }

    // route parameters are the only arguments known for sure until schemas come from request types
    private static JsonElement InputSchema(EndpointTool tool)
    {
        var routeParameters = tool.Endpoint.RoutePattern.Parameters.Select(p => p.Name).ToArray();
        var schema = new
        {
            type = "object",
            properties = routeParameters.ToDictionary(name => name, _ => new { type = "string" }),
            required = routeParameters,
        };

        return JsonSerializer.SerializeToElement(schema);
    }
}
