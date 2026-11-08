using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Testing;

public static class McpToolCallExtensions
{
    /// <summary>
    /// Calls a tool and returns its structured content, or its text parsed as JSON.
    /// A tool error throws with the text the model would have seen, so a failing test says why.
    /// </summary>
    public static async Task<JsonElement> CallToolForJsonAsync(
        this McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        if (result.IsError == true)
        {
            throw new McpToolCallException(toolName, text);
        }

        if (result.StructuredContent is { } structured)
        {
            return structured.Clone();
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
