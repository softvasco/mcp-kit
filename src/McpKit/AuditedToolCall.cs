using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace McpKit;

/// <summary>Runs a tool call and records it in the audit log, whatever the outcome.</summary>
internal static class AuditedToolCall
{
    public static async ValueTask<CallToolResult> RunAsync(
        string toolName,
        IServiceProvider services,
        ClaimsPrincipal user,
        IReadOnlyDictionary<string, JsonElement> call,
        Func<Task<(CallToolResult Result, ToolCallOutcome Outcome, int? Status)>> run,
        CancellationToken cancellationToken)
    {
        var clock = services.GetRequiredService<TimeProvider>();
        var audit = services.GetRequiredService<IToolCallAudit>();
        var startedAt = clock.GetUtcNow();
        var started = clock.GetTimestamp();

        ToolCallRecord Record(ToolCallOutcome outcome, int? status, CallToolResult? result) => new(
            toolName,
            CallFingerprint.Caller(user),
            CallFingerprint.Arguments(call),
            outcome,
            status,
            result?.Content.OfType<TextContentBlock>().Sum(t => Encoding.UTF8.GetByteCount(t.Text)) ?? 0,
            clock.GetElapsedTime(started),
            startedAt);

        try
        {
            var (result, outcome, status) = await run();
            await audit.RecordAsync(Record(outcome, status, result), cancellationToken);
            return result;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await audit.RecordAsync(Record(ToolCallOutcome.Failed, null, null), CancellationToken.None);
            throw;
        }
    }
}
