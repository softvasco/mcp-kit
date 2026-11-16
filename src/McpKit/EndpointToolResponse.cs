namespace McpKit;

/// <summary>What the endpoint wrote for a tool call: status, content type and body.</summary>
public sealed record EndpointToolResponse(int StatusCode, string? ContentType, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    // set when the endpoint's policy refused the caller and the endpoint never ran
    internal bool RefusedByPolicy { get; init; }
}
