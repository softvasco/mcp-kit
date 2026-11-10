using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpKit;

/// <summary>An MCP resource backed by a GET endpoint; a read runs the endpoint.</summary>
internal sealed class EndpointMcpServerResource(EndpointResource resource) : McpServerResource
{
    private const string Json = "application/json";

    public override ResourceTemplate ProtocolResourceTemplate { get; } = new()
    {
        Name = resource.Name,
        Description = resource.Description,
        UriTemplate = resource.Uri.Template,
        MimeType = Json,
    };

    public override IReadOnlyList<object> Metadata => [.. resource.Endpoint.Metadata];

    public override bool IsMatch(string uri) => resource.Uri.Match(uri) is not null;

    public override async ValueTask<ReadResourceResult> ReadAsync(
        RequestContext<ReadResourceRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var uri = request.Params?.Uri ?? throw new McpProtocolException("The read has no URI.", McpErrorCode.InvalidParams);
        var match = resource.Uri.Match(uri) ?? throw new McpProtocolException($"'{uri}' is not a {resource.Name} URI.", McpErrorCode.ResourceNotFound);
        var services = request.Services ?? throw new InvalidOperationException("The MCP request has no service provider.");
        var user = request.User ?? new ClaimsPrincipal(new ClaimsIdentity());

        var endpoint = new EndpointTool(resource.Name, resource.Description, HttpMethods.Get, resource.Endpoint);
        var response = await EndpointToolInvoker.InvokeAsync(endpoint, match.Arguments, services, user, cancellationToken);
        var redaction = services.GetRequiredService<McpKitOptions>().Redaction
            .CombinedWith(resource.Endpoint.Metadata.GetMetadata<ToolRedaction>());

        return new ReadResourceResult
        {
            Contents = [new TextResourceContents { Uri = uri, MimeType = Json, Text = ToolOutput.Format(response, redaction) }],
        };
    }
}
