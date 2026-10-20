using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace McpKit;

internal enum ConfirmationCheck
{
    Valid,
    Invalid,
    Expired,
}

/// <summary>Issues and checks confirmation tokens for one tool, one caller and one exact set of arguments.</summary>
// the server is stateless, so the token carries everything and data protection keeps it from being forged
internal sealed class ToolConfirmations(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector _protector = protection.CreateProtector("McpKit.ToolConfirmation.v1");

    public (string Token, DateTimeOffset ExpiresAt) Issue(
        string tool, ClaimsPrincipal user, IReadOnlyDictionary<string, JsonElement> arguments, TimeSpan lifetime)
    {
        var expiresAt = clock.GetUtcNow() + lifetime;
        var payload = new Payload(tool, CallerOf(user), Fingerprint(arguments), expiresAt.ToUnixTimeMilliseconds());
        return (_protector.Protect(JsonSerializer.Serialize(payload)), expiresAt);
    }

    public ConfirmationCheck Check(
        string token, string tool, ClaimsPrincipal user, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return ConfirmationCheck.Invalid;
        }

        if (payload is null
            || payload.Tool != tool
            || payload.Caller != CallerOf(user)
            || payload.Arguments != Fingerprint(arguments))
        {
            return ConfirmationCheck.Invalid;
        }

        return clock.GetUtcNow().ToUnixTimeMilliseconds() > payload.ExpiresAt ? ConfirmationCheck.Expired : ConfirmationCheck.Valid;
    }

    private static string CallerOf(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? user.Identity?.Name ?? string.Empty;

    // key order is the client's choice, so sort before hashing; values are compared as compact JSON
    private static string Fingerprint(IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var canonical = new StringBuilder();
        foreach (var (name, value) in arguments.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            canonical.Append(name).Append('=').Append(JsonSerializer.Serialize(value)).Append('\n');
        }

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private sealed record Payload(string Tool, string Caller, string Arguments, long ExpiresAt);
}
