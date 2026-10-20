using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace McpKit.Tests;

public class ToolAuthorizationOverMcpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_client_only_sees_the_tools_its_scopes_allow()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app, "accounts:read");

        var tools = await client.ListToolsAsync(cancellationToken: Token);

        Assert.Equal(["get_account", "status"], tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_client_with_the_write_scope_sees_the_write_tool()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app, "accounts:read accounts:write");

        var tools = await client.ListToolsAsync(cancellationToken: Token);

        Assert.Contains(tools, t => t.Name == "deposit");
    }

    [Fact]
    public async Task Calling_a_hidden_tool_fails_like_calling_one_that_does_not_exist()
    {
        await using var app = await StartHost();
        await using var client = await Connect(app, "accounts:read");

        await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("deposit", new Dictionary<string, object?> { ["id"] = "acc-1" }, cancellationToken: Token).AsTask());
    }

    private static async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(ScopesHeader.Name).AddScheme<AuthenticationSchemeOptions, ScopesHeader>(ScopesHeader.Name, null);
        builder.Services.AddAuthorization();
        builder.Services.AddMcpKit(options => options.AllowWriteTools = true);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/status", () => "up").WithName("Status").AsMcpTool();
        app.MapGet("/accounts/{id}", (string id) => Results.Ok(new { id }))
            .RequireAuthorization(policy => policy.RequireClaim("scope", "accounts:read"))
            .WithName("GetAccount").AsMcpTool();
        app.MapPost("/accounts/{id}/deposits", (string id) => Results.Ok(new { id }))
            .RequireAuthorization(policy => policy.RequireClaim("scope", "accounts:write"))
            .WithName("Deposit").AsMcpWriteTool();
        app.MapMcpKit().RequireAuthorization();

        await app.StartAsync(Token);
        return app;
    }

    private static async Task<McpClient> Connect(WebApplication app, string scopes)
    {
        var http = app.GetTestClient();
        http.DefaultRequestHeaders.Add(ScopesHeader.Header, scopes);
        var options = new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") };
        return await McpClient.CreateAsync(new HttpClientTransport(options, http, ownsHttpClient: true), cancellationToken: Token);
    }

    // stands in for a bearer token: the scopes come straight from a header
    private sealed class ScopesHeader(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
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
}
