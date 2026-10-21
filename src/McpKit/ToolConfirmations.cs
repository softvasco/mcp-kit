using System.Security.Claims;
using System.Security.Cryptography;
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
        var payload = new Payload(tool, CallFingerprint.Caller(user), CallFingerprint.Arguments(arguments), expiresAt.ToUnixTimeMilliseconds());
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
            || payload.Caller != CallFingerprint.Caller(user)
            || payload.Arguments != CallFingerprint.Arguments(arguments))
        {
            return ConfirmationCheck.Invalid;
        }

        return clock.GetUtcNow().ToUnixTimeMilliseconds() > payload.ExpiresAt ? ConfirmationCheck.Expired : ConfirmationCheck.Valid;
    }

    private sealed record Payload(string Tool, string Caller, string Arguments, long ExpiresAt);
}
