using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpKit.Tests;

// stands in for a bearer token: the scopes come straight from a header
internal sealed class ScopesHeader(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "Scopes";
    public const string Header = "X-Test-Scopes";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers[Header].ToString() is not { Length: > 0 } scopes)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = scopes.Split(' ').Select(s => new Claim("scope", s)).Append(new Claim(ClaimTypes.Name, "agent"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
    }
}
