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
        var telemetry = services.GetRequiredService<ToolCallTelemetry>();
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
            var record = Record(outcome, status, result);
            telemetry.Record(record);
            await audit.RecordAsync(record, cancellationToken);
            return result;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            var record = Record(ToolCallOutcome.Failed, null, null);
            telemetry.Record(record);
            await audit.RecordAsync(record, CancellationToken.None);
            throw;
        }
    }
}
