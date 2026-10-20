using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;

namespace McpKit.Tests;

public class ToolConfirmationsTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly FakeTimeProvider _clock = new();
    private readonly ToolConfirmations _confirmations;

    public ToolConfirmationsTests() =>
        _confirmations = new ToolConfirmations(new EphemeralDataProtectionProvider(), _clock);

    [Fact]
    public void A_token_confirms_the_same_call()
    {
        var (token, _) = _confirmations.Issue("deposit", User("alice"), Args("""{"id":"acc-1","amount":25}"""), Lifetime);

        Assert.Equal(ConfirmationCheck.Valid, _confirmations.Check(token, "deposit", User("alice"), Args("""{"amount":25,"id":"acc-1"}""")));
    }

    [Fact]
    public void Changing_an_argument_after_the_preview_voids_the_token()
    {
        var (token, _) = _confirmations.Issue("deposit", User("alice"), Args("""{"id":"acc-1","amount":25}"""), Lifetime);

        Assert.Equal(ConfirmationCheck.Invalid, _confirmations.Check(token, "deposit", User("alice"), Args("""{"id":"acc-1","amount":2500}""")));
    }

    [Fact]
    public void A_token_is_only_good_for_the_tool_and_caller_it_was_issued_to()
    {
        var args = Args("""{"id":"acc-1","amount":25}""");
        var (token, _) = _confirmations.Issue("deposit", User("alice"), args, Lifetime);

        Assert.Equal(ConfirmationCheck.Invalid, _confirmations.Check(token, "withdraw", User("alice"), args));
        Assert.Equal(ConfirmationCheck.Invalid, _confirmations.Check(token, "deposit", User("bob"), args));
    }

    [Fact]
    public void A_token_runs_out_after_its_lifetime()
    {
        var args = Args("""{"id":"acc-1"}""");
        var (token, expiresAt) = _confirmations.Issue("deposit", User("alice"), args, Lifetime);

        _clock.Advance(Lifetime + TimeSpan.FromSeconds(1));

        Assert.Equal(_clock.GetUtcNow() - TimeSpan.FromSeconds(1), expiresAt);
        Assert.Equal(ConfirmationCheck.Expired, _confirmations.Check(token, "deposit", User("alice"), args));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    public void Anything_else_is_not_a_token(string token)
    {
        Assert.Equal(ConfirmationCheck.Invalid, _confirmations.Check(token, "deposit", User("alice"), Args("{}")));
    }

    private static ClaimsPrincipal User(string id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));

    private static Dictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
