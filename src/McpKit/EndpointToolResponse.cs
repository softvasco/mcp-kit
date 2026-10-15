namespace McpKit;

/// <summary>What the endpoint wrote for a tool call: status, content type and body.</summary>
public sealed record EndpointToolResponse(int StatusCode, string? ContentType, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
