using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace McpKit;

/// <summary>The results every kind of tool hands back, so they read the same to a model.</summary>
internal static class ToolResults
{
    // clients that read structuredContent get the object itself; the text block holds the same JSON
    public static CallToolResult Output(string json, bool isError)
    {
        var result = Text(json, isError);
        var value = JsonElement.Parse(json);
        if (value.ValueKind == JsonValueKind.Object)
        {
            result.StructuredContent = value;
        }

        return result;
    }

    public static CallToolResult Refused(string code, string title) =>
        Text(JsonSerializer.Serialize(new { title, code }), isError: true);

    // whole seconds, rounded up, so a client that waits exactly that long gets in
    public static CallToolResult RateLimited(TimeSpan retryAfter) =>
        Text(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["title"] = "Too many calls to this tool. Wait before calling it again.",
            ["code"] = "tool.rate_limited",
            ["retry_after_seconds"] = (int)Math.Ceiling(retryAfter.TotalSeconds),
        }), isError: true);

    public static CallToolResult Text(string text, bool isError) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = isError,
    };

    // hints only: clients use them to decide when to ask the user, nothing enforces them
    public static ToolAnnotations Annotate(bool changesState, string httpMethod)
    {
        if (!changesState)
        {
            return new ToolAnnotations { ReadOnlyHint = true };
        }

        var replaces = string.Equals(httpMethod, "DELETE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(httpMethod, "PUT", StringComparison.OrdinalIgnoreCase);
        return new ToolAnnotations { ReadOnlyHint = false, DestructiveHint = replaces, IdempotentHint = replaces };
    }
}
