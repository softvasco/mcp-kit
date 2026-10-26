using System.Security.Claims;
using System.Text;
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
        var call = arguments.Where(a => a.Key != McpToolConfirmationMetadata.Argument).ToDictionary();

        var clock = services.GetRequiredService<TimeProvider>();
        var audit = services.GetRequiredService<IToolCallAudit>();
        var startedAt = clock.GetUtcNow();
        var started = clock.GetTimestamp();

        ToolCallRecord Record(ToolCallOutcome outcome, int? status, CallToolResult? result) => new(
            tool.Name,
            CallFingerprint.Caller(user),
            CallFingerprint.Arguments(call),
            outcome,
            status,
            result?.Content.OfType<TextContentBlock>().Sum(t => Encoding.UTF8.GetByteCount(t.Text)) ?? 0,
            clock.GetElapsedTime(started),
            startedAt);

        try
        {
            var (result, outcome, status) = await RunAsync(arguments, call, services, user, cancellationToken);
            await audit.RecordAsync(Record(outcome, status, result), cancellationToken);
            return result;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await audit.RecordAsync(Record(ToolCallOutcome.Failed, null, null), CancellationToken.None);
            throw;
        }
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
            return (RateLimited(retryAfter), ToolCallOutcome.RateLimited, null);
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
                    return (Refused("tool.confirmation_expired", "The confirmation token has expired. Call again without it for a new one."),
                        ToolCallOutcome.ConfirmationRefused, null);
                case ConfirmationCheck.Invalid:
                    return (Refused("tool.confirmation_invalid", "The confirmation token is not for this call. Call again without it for a new one."),
                        ToolCallOutcome.ConfirmationRefused, null);
            }

            arguments = call;
        }

        var response = await EndpointToolInvoker.InvokeAsync(tool, arguments.AsReadOnly(), services, user, cancellationToken);
        var redaction = services.GetRequiredService<McpKitOptions>().Redaction
            .CombinedWith(tool.Endpoint.Metadata.GetMetadata<ToolRedaction>());

        return (
            Output(ToolOutput.Format(response, redaction), isError: !response.IsSuccess),
            response.IsSuccess ? ToolCallOutcome.Succeeded : ToolCallOutcome.Failed,
            response.StatusCode);
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

    // whole seconds, rounded up, so a client that waits exactly that long gets in
    private static CallToolResult RateLimited(TimeSpan retryAfter) =>
        Text(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["title"] = "Too many calls to this tool. Wait before calling it again.",
            ["code"] = "tool.rate_limited",
            ["retry_after_seconds"] = (int)Math.Ceiling(retryAfter.TotalSeconds),
        }), isError: true);

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
