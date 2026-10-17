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
        InputSchema = ToolInputSchema.For(tool.Endpoint),
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
}
