using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
        Annotations = Annotate(tool),
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

        if (tool.Endpoint.Metadata.GetMetadata<McpToolConfirmationMetadata>() is { } confirmation)
        {
            var confirmations = services.GetRequiredService<ToolConfirmations>();
            var call = arguments.Where(a => a.Key != McpToolConfirmationMetadata.Argument).ToDictionary();
            if (!arguments.TryGetValue(McpToolConfirmationMetadata.Argument, out var token)
                || token.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(token.GetString()))
            {
                return Pending(confirmations.Issue(tool.Name, user, call, confirmation.Lifetime), call);
            }

            switch (confirmations.Check(token.GetString()!, tool.Name, user, call))
            {
                case ConfirmationCheck.Expired:
                    return Refused("tool.confirmation_expired", "The confirmation token has expired. Call again without it for a new one.");
                case ConfirmationCheck.Invalid:
                    return Refused("tool.confirmation_invalid", "The confirmation token is not for this call. Call again without it for a new one.");
            }

            arguments = call;
        }

        var response = await EndpointToolInvoker.InvokeAsync(tool, arguments.AsReadOnly(), services, user, cancellationToken);

        return Output(ToolOutput.Format(response), isError: !response.IsSuccess);
    }

    // clients that read structuredContent get the object itself; the text block holds the same JSON
    private static CallToolResult Output(string json, bool isError)
    {
        var result = Text(json, isError);
        var value = JsonElement.Parse(json);
        if (value.ValueKind == JsonValueKind.Object)
        {
            result.StructuredContent = value;
        }

        return result;
    }

    // not an error: the call did what it should, which is nothing until someone says yes
    private CallToolResult Pending((string Token, DateTimeOffset ExpiresAt) issued, Dictionary<string, JsonElement> call) =>
        Text(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["status"] = "confirmation_required",
            ["message"] = "Nothing has run. Show the user this call and, if they agree, call again with the same arguments and confirmation_token.",
            ["tool"] = tool.Name,
            ["arguments"] = call,
            [McpToolConfirmationMetadata.Argument] = issued.Token,
            ["expires_at"] = issued.ExpiresAt,
        }), isError: false);

    private static CallToolResult Refused(string code, string title) =>
        Text(JsonSerializer.Serialize(new { title, code }), isError: true);

    private static CallToolResult Text(string text, bool isError) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = isError,
    };

    // hints only: clients use them to decide when to ask the user, nothing enforces them
    private static ToolAnnotations Annotate(EndpointTool tool) => tool.ChangesState
        ? new ToolAnnotations
        {
            ReadOnlyHint = false,
            DestructiveHint = HttpMethods.IsDelete(tool.HttpMethod) || HttpMethods.IsPut(tool.HttpMethod),
            IdempotentHint = HttpMethods.IsDelete(tool.HttpMethod) || HttpMethods.IsPut(tool.HttpMethod),
        }
        : new ToolAnnotations { ReadOnlyHint = true };
}
