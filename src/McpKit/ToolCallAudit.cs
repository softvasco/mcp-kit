using Microsoft.Extensions.Logging;

namespace McpKit;

public enum ToolCallOutcome
{
    Succeeded,
    Failed,
    ConfirmationRequested,
    ConfirmationRefused,
    RateLimited,
}

/// <summary>One tool call for the audit: the arguments' hash, never their values.</summary>
public sealed record ToolCallRecord(
    string Tool,
    string Caller,
    string ArgumentsHash,
    ToolCallOutcome Outcome,
    int? StatusCode,
    int ResultBytes,
    TimeSpan Duration,
    DateTimeOffset StartedAt);

/// <summary>Gets every endpoint tool call. Replace it to send calls somewhere else.</summary>
public interface IToolCallAudit
{
    ValueTask RecordAsync(ToolCallRecord toolCall, CancellationToken cancellationToken);
}

internal sealed partial class LoggerToolCallAudit(ILogger<LoggerToolCallAudit> logger) : IToolCallAudit
{
    public ValueTask RecordAsync(ToolCallRecord toolCall, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        Called(
            logger,
            toolCall.Tool,
            toolCall.Caller,
            toolCall.Outcome,
            toolCall.StatusCode,
            toolCall.ArgumentsHash,
            toolCall.ResultBytes,
            toolCall.Duration.TotalMilliseconds);
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "ToolCalled",
        Level = LogLevel.Information,
        Message = "Tool {Tool} called by {Caller}: {Outcome} ({StatusCode}), arguments {ArgumentsHash}, {ResultBytes} bytes in {DurationMs} ms")]
    private static partial void Called(
        ILogger logger,
        string tool,
        string caller,
        ToolCallOutcome outcome,
        int? statusCode,
        string argumentsHash,
        int resultBytes,
        double durationMs);
}
