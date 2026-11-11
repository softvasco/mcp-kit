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
        var services = request.Services ?? throw new InvalidOperationException("The MCP request has no service provider.");
        var user = request.User ?? new ClaimsPrincipal(new ClaimsIdentity());

        return new ReadResourceResult { Contents = [await ReadAsync(resource, uri, services, user, cancellationToken)] };
    }

    /// <summary>Runs the endpoint behind a resource URI, as a read or for a prompt that attaches it.</summary>
    internal static async Task<TextResourceContents> ReadAsync(
        EndpointResource resource, string uri, IServiceProvider services, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var match = resource.Uri.Match(uri) ?? throw new McpProtocolException($"'{uri}' is not a {resource.Name} URI.", McpErrorCode.ResourceNotFound);
        if (match.UnknownQuery.Count > 0)
        {
            var takes = resource.Uri.Query.Count == 0 ? "no query parameters" : string.Join(", ", resource.Uri.Query);
            throw new McpProtocolException(
                $"{resource.Name} doesn't take {string.Join(", ", match.UnknownQuery)}. It takes {takes}.", McpErrorCode.InvalidParams);
        }

        var endpoint = new EndpointTool(resource.Name, resource.Description, HttpMethods.Get, resource.Endpoint);
        var response = await EndpointToolInvoker.InvokeAsync(endpoint, match.Arguments, services, user, cancellationToken);
        var redaction = services.GetRequiredService<McpKitOptions>().Redaction
            .CombinedWith(resource.Endpoint.Metadata.GetMetadata<ToolRedaction>());
        var text = ToolOutput.Format(response, redaction);

        // a read has no error result like a tool call, so the endpoint's problem goes in a protocol error
        if (!response.IsSuccess)
        {
            throw new McpProtocolException(text, ErrorCode(response.StatusCode));
        }

        return new TextResourceContents { Uri = uri, MimeType = Json, Text = text };
    }

    private static McpErrorCode ErrorCode(int status) => status switch
    {
        StatusCodes.Status404NotFound => McpErrorCode.ResourceNotFound,
        StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity => McpErrorCode.InvalidParams,
        < 500 => McpErrorCode.InvalidRequest,
        _ => McpErrorCode.InternalError,
    };
}
