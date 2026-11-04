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
        Annotations = ToolResults.Annotate(tool.ChangesState, tool.HttpMethod),
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
        var call = arguments.Where(a => a.Key != McpToolConfirmationMetadata.Argument).ToDictionary();

        return await AuditedToolCall.RunAsync(
            tool.Name, services, user, call, () => RunAsync(arguments, call, services, user, cancellationToken), cancellationToken);
    }

    private async Task<(CallToolResult Result, ToolCallOutcome Outcome, int? Status)> RunAsync(
        IDictionary<string, JsonElement> arguments,
        Dictionary<string, JsonElement> call,
        IServiceProvider services,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var limit = tool.Endpoint.Metadata.GetMetadata<ToolRateLimit>() ?? services.GetRequiredService<McpKitOptions>().RateLimit;
        if (limit is not null
            && services.GetRequiredService<ToolRateLimiter>().Acquire(CallFingerprint.Caller(user), tool.Name, limit) is { } retryAfter)
        {
            return (ToolResults.RateLimited(retryAfter), ToolCallOutcome.RateLimited, null);
        }

        if (tool.Endpoint.Metadata.GetMetadata<McpToolConfirmationMetadata>() is { } confirmation)
        {
            var confirmations = services.GetRequiredService<ToolConfirmations>();
            if (!arguments.TryGetValue(McpToolConfirmationMetadata.Argument, out var token)
                || token.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(token.GetString()))
            {
                var pending = Pending(confirmations.Issue(tool.Name, user, call, confirmation.Lifetime), call);
                return (pending, ToolCallOutcome.ConfirmationRequested, null);
            }

            switch (confirmations.Check(token.GetString()!, tool.Name, user, call))
            {
                case ConfirmationCheck.Expired:
                    return (ToolResults.Refused("tool.confirmation_expired", "The confirmation token has expired. Call again without it for a new one."),
                        ToolCallOutcome.ConfirmationRefused, null);
                case ConfirmationCheck.Invalid:
                    return (ToolResults.Refused("tool.confirmation_invalid", "The confirmation token is not for this call. Call again without it for a new one."),
                        ToolCallOutcome.ConfirmationRefused, null);
            }

            arguments = call;
        }

        var response = await EndpointToolInvoker.InvokeAsync(tool, arguments.AsReadOnly(), services, user, cancellationToken);
        var redaction = services.GetRequiredService<McpKitOptions>().Redaction
            .CombinedWith(tool.Endpoint.Metadata.GetMetadata<ToolRedaction>());

        return (
            ToolResults.Output(ToolOutput.Format(response, redaction), isError: !response.IsSuccess),
            response.IsSuccess ? ToolCallOutcome.Succeeded : ToolCallOutcome.Failed,
            response.StatusCode);
    }

    // not an error: the call did what it should, which is nothing until someone says yes
    private CallToolResult Pending((string Token, DateTimeOffset ExpiresAt) issued, Dictionary<string, JsonElement> call) =>
        ToolResults.Text(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["status"] = "confirmation_required",
            ["message"] = "Nothing has run. Show the user this call and, if they agree, call again with the same arguments and confirmation_token.",
            ["tool"] = tool.Name,
            ["arguments"] = call,
            [McpToolConfirmationMetadata.Argument] = issued.Token,
            ["expires_at"] = issued.ExpiresAt,
        }), isError: false);
}
