using System.Net.Http.Headers;
using System.Security.Claims;

namespace McpKit.OpenApi;

/// <summary>Picks the upstream credentials for one MCP caller; null sends none.</summary>
public delegate ValueTask<AuthenticationHeaderValue?> UpstreamCredentials(
    ClaimsPrincipal caller, IServiceProvider services, CancellationToken cancellationToken);
