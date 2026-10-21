using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace McpKit;

/// <summary>Who made a tool call and a hash of what they sent, the same way everywhere.</summary>
internal static class CallFingerprint
{
    public static string Caller(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? user.Identity?.Name ?? string.Empty;

    // key order is the client's choice, so sort before hashing; values are compared as compact JSON
    public static string Arguments(IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var canonical = new StringBuilder();
        foreach (var (name, value) in arguments.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            canonical.Append(name).Append('=').Append(JsonSerializer.Serialize(value)).Append('\n');
        }

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}
