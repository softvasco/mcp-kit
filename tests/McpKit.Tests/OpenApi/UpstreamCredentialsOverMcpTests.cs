using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using McpKit.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace McpKit.Tests.OpenApi;

public sealed class UpstreamCredentialsOverMcpTests
{
    private const string ClientToken = "Bearer token-for-the-mcp-server";

    private readonly List<string?> _upstreamAuthorization = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_upstream_api_gets_the_credentials_picked_for_the_caller()
    {
        await using var app = await StartHost(builder => builder.WithUpstreamCredentials("bank", (caller, _, _) =>
            ValueTask.FromResult<AuthenticationHeaderValue?>(new("Bearer", $"exchanged-for-{caller.Identity?.Name}"))));
        await using var client = await Connect(app, "alice");

        await client.CallToolAsync("get_account", new Dictionary<string, object?> { ["id"] = "acc-1" }, cancellationToken: Token);

        Assert.Equal("Bearer exchanged-for-alice", Assert.Single(_upstreamAuthorization));
    }

    [Fact]
    public async Task Without_credentials_the_clients_token_still_stays_on_the_mcp_server()
    {
        await using var app = await StartHost(_ => { });
        await using var client = await Connect(app, "alice");

        await client.CallToolAsync("get_account", new Dictionary<string, object?> { ["id"] = "acc-1" }, cancellationToken: Token);

        Assert.Null(Assert.Single(_upstreamAuthorization));
    }

    private async Task<WebApplication> StartHost(Action<IMcpServerBuilder> credentials)
    {
        var tools = await SampleDocument.LoadToolsAsync(Token);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(UserHeader.Name).AddScheme<AuthenticationSchemeOptions, UserHeader>(UserHeader.Name, null);
        builder.Services.AddAuthorization();
        var mcp = builder.Services
            .AddMcpKit()
            .AddOpenApiTools("bank", tools, http => http.BaseAddress = new Uri("https://bank.example/"));
        credentials(mcp);
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => new Upstream(_upstreamAuthorization)));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMcpKit().RequireAuthorization();
        await app.StartAsync(Token);
        return app;
    }

    private static async Task<McpClient> Connect(WebApplication app, string user)
    {
        var http = app.GetTestClient();
        http.DefaultRequestHeaders.Add(UserHeader.Header, user);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", ClientToken);
        var options = new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") };
        return await McpClient.CreateAsync(new HttpClientTransport(options, http, ownsHttpClient: true), cancellationToken: Token);
    }

    private sealed class Upstream(List<string?> authorization) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            authorization.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"acc-1"}""", Encoding.UTF8, "application/json"),
            });
        }
    }

    // stands in for the server's real token validation: the user name comes straight from a header
    private sealed class UserHeader(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "User";
        public const string Header = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers[Header].ToString() is not { Length: > 0 } user)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }
}
