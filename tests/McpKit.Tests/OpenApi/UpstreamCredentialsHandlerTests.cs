using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using McpKit.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace McpKit.Tests.OpenApi;

public sealed class UpstreamCredentialsHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_credentials_are_picked_for_the_caller_of_the_tool()
    {
        var seen = new Recorder();
        using var client = Client(seen, (caller, _, _) =>
            ValueTask.FromResult<AuthenticationHeaderValue?>(new("Bearer", $"for-{caller.Identity?.Name}")));

        await Send(client, Caller("alice"));

        Assert.Equal("Bearer for-alice", seen.Authorization);
    }

    [Fact]
    public async Task An_authorization_header_already_on_the_request_is_replaced()
    {
        var seen = new Recorder();
        using var client = Client(seen, (_, _, _) => ValueTask.FromResult<AuthenticationHeaderValue?>(new("Bearer", "exchanged")));
        using var request = Request(Caller("alice"));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer from-the-agent");

        using var response = await client.SendAsync(request, Token);

        Assert.Equal("Bearer exchanged", seen.Authorization);
    }

    [Fact]
    public async Task No_credentials_means_no_authorization_header()
    {
        var seen = new Recorder();
        using var client = Client(seen, (_, _, _) => ValueTask.FromResult<AuthenticationHeaderValue?>(null));
        using var request = Request(Caller("alice"));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer from-the-agent");

        using var response = await client.SendAsync(request, Token);

        Assert.Null(seen.Authorization);
    }

    [Fact]
    public async Task A_request_without_a_caller_is_treated_as_anonymous()
    {
        var seen = new Recorder();
        ClaimsPrincipal? asked = null;
        using var client = Client(seen, (caller, _, _) =>
        {
            asked = caller;
            return ValueTask.FromResult<AuthenticationHeaderValue?>(null);
        });

        await Send(client, caller: null);

        Assert.False(asked?.Identity?.IsAuthenticated);
    }

    private static HttpClient Client(Recorder seen, UpstreamCredentials credentials)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var handler = new UpstreamCredentialsHandler(credentials, services) { InnerHandler = seen };
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
    }

    private static async Task Send(HttpClient client, ClaimsPrincipal? caller)
    {
        using var request = Request(caller);
        using var response = await client.SendAsync(request, Token);
    }

    private static HttpRequestMessage Request(ClaimsPrincipal? caller)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "accounts");
        if (caller is not null)
        {
            request.Options.Set(UpstreamCredentialsHandler.Caller, caller);
        }

        return request;
    }

    private static ClaimsPrincipal Caller(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "test"));

    private sealed class Recorder : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Answer(request));

        private HttpResponseMessage Answer(HttpRequestMessage request)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
