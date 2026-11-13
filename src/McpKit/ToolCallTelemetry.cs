using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace McpKit;

/// <summary>The meter a host passes to AddMeter to collect tool call metrics.</summary>
public static class McpKitTelemetry
{
    public const string MeterName = "McpKit";
}

// the SDK already times tools/call; it can't tell a call that ran from one that hit a limit
internal sealed class ToolCallTelemetry
{
    private const string ToolTag = "gen_ai.tool.name";
    private const string OutcomeTag = "mcpkit.tool.outcome";
    private const string StatusTag = "http.response.status_code";

    private readonly Counter<long> _calls;
    private readonly Histogram<double> _duration;

    public ToolCallTelemetry(IMeterFactory meters)
    {
        var meter = meters.Create(McpKitTelemetry.MeterName);
        _calls = meter.CreateCounter<long>("mcpkit.tool.calls", "{call}", "Endpoint tool calls by outcome.");
        _duration = meter.CreateHistogram<double>("mcpkit.tool.duration", "s", "Time spent on an endpoint tool call.");
    }

    public void Record(ToolCallRecord call)
    {
        var outcome = Name(call.Outcome);
        var tags = new TagList { { ToolTag, call.Tool }, { OutcomeTag, outcome } };
        _calls.Add(1, tags);
        _duration.Record(call.Duration.TotalSeconds, tags);

        // the current span is the SDK's tools/call span whenever someone is tracing it
        if (Activity.Current is { } span)
        {
            span.SetTag(OutcomeTag, outcome);
            if (call.StatusCode is { } status)
            {
                span.SetTag(StatusTag, status);
            }
        }
    }

    private static string Name(ToolCallOutcome outcome) => outcome switch
    {
        ToolCallOutcome.Succeeded => "succeeded",
        ToolCallOutcome.Failed => "failed",
        ToolCallOutcome.ConfirmationRequested => "confirmation_requested",
        ToolCallOutcome.ConfirmationRefused => "confirmation_refused",
        ToolCallOutcome.RateLimited => "rate_limited",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
