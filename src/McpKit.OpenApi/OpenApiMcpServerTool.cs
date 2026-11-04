using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpKit.OpenApi;

/// <summary>An MCP tool generated from an OpenAPI operation; a call goes to the upstream API.</summary>
internal sealed class OpenApiMcpServerTool(OpenApiTool tool, string clientName) : McpServerTool
{
    public override Tool ProtocolTool { get; } = new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = tool.InputSchema,
        Annotations = ToolResults.Annotate(tool.ChangesState, tool.Method.Method),
    };

    public override IReadOnlyList<object> Metadata => [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var services = request.Services ?? throw new InvalidOperationException("The MCP request has no service provider.");
        var arguments = (request.Params?.Arguments ?? new Dictionary<string, JsonElement>()).ToDictionary();
        var user = request.User ?? new ClaimsPrincipal(new ClaimsIdentity());

        return await AuditedToolCall.RunAsync(
            tool.Name, services, user, arguments, () => RunAsync(arguments, services, user, cancellationToken), cancellationToken);
    }

    private async Task<(CallToolResult Result, ToolCallOutcome Outcome, int? Status)> RunAsync(
        Dictionary<string, JsonElement> arguments,
        IServiceProvider services,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var options = services.GetRequiredService<McpKitOptions>();
        if (options.RateLimit is { } limit
            && services.GetRequiredService<ToolRateLimiter>().Acquire(CallFingerprint.Caller(user), tool.Name, limit) is { } retryAfter)
        {
            return (ToolResults.RateLimited(retryAfter), ToolCallOutcome.RateLimited, null);
        }

        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        var response = await OpenApiToolInvoker.InvokeAsync(tool, arguments, client, cancellationToken);

        return (
            ToolResults.Output(ToolOutput.Format(response, options.Redaction), isError: !response.IsSuccess),
            response.IsSuccess ? ToolCallOutcome.Succeeded : ToolCallOutcome.Failed,
            response.StatusCode);
    }
}
