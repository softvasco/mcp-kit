using System.Security.Claims;

namespace McpKit.OpenApi;

internal sealed class UpstreamCredentialsHandler(UpstreamCredentials credentials, IServiceProvider services) : DelegatingHandler
{
    internal static readonly HttpRequestOptionsKey<ClaimsPrincipal> Caller = new("McpKit.OpenApi.Caller");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var caller = request.Options.TryGetValue(Caller, out var user) ? user : new ClaimsPrincipal(new ClaimsIdentity());

        // the hook owns this header, so a header argument the agent filled in can't put its own token here
        request.Headers.Authorization = await credentials(caller, services, cancellationToken);

        return await base.SendAsync(request, cancellationToken);
    }
}
