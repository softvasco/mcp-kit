using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace McpKit;

/// <summary>Runs a tool's endpoint in-process, with arguments as route, query and body.</summary>
public static class EndpointToolInvoker
{
    public static async Task<EndpointToolResponse> InvokeAsync(
        EndpointTool tool,
        IReadOnlyDictionary<string, JsonElement> arguments,
        IServiceProvider services,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(user);

        var endpoint = tool.Endpoint;

        var handler = endpoint.RequestDelegate
            ?? throw new InvalidOperationException($"Tool '{tool.Name}' has no request delegate.");

        var routeParameters = endpoint.RoutePattern.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var headers = endpoint.Metadata.GetOrderedMetadata<McpToolHeaderMetadata>().ToDictionary(h => h.Argument, h => h.Header, StringComparer.OrdinalIgnoreCase);
        var others = arguments.Where(a => !routeParameters.Contains(a.Key) && !headers.ContainsKey(a.Key)).ToList();

        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = user,
            RequestAborted = cancellationToken,
        };
        context.SetEndpoint(endpoint);

        var request = context.Request;
        request.Method = tool.HttpMethod;
        foreach (var (name, value) in arguments.Where(a => routeParameters.Contains(a.Key)))
        {
            request.RouteValues[name] = AsText(value);
        }

        request.Path = BuildPath(endpoint.RoutePattern, request.RouteValues);

        foreach (var (argument, value) in arguments.Where(a => headers.ContainsKey(a.Key)))
        {
            request.Headers[headers[argument]] = AsText(value);
        }

        if (CarriesBody(tool.HttpMethod))
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(others.ToDictionary(a => a.Key, a => a.Value));
            request.ContentType = "application/json";
            request.ContentLength = json.Length;
            request.Body = new MemoryStream(json);

            // minimal APIs see no body unless the server says the request can have one
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new HasBody());
        }
        else
        {
            var query = new QueryBuilder();
            foreach (var (name, value) in others)
            {
                query.Add(name, AsQueryValues(value));
            }

            request.QueryString = query.ToQueryString();
        }

        if (await AuthorizeAsync(context) is { } refused)
        {
            return refused;
        }

        await using var body = new MemoryStream();
        context.Response.Body = body;

        await handler(context);

        return new EndpointToolResponse(context.Response.StatusCode, context.Response.ContentType, Encoding.UTF8.GetString(body.ToArray()));
    }

    // in-process calls skip the authorization middleware, so the endpoint's policy is checked here
    private static async Task<EndpointToolResponse?> AuthorizeAsync(HttpContext context)
    {
        var metadata = context.GetEndpoint()!.Metadata;
        var protectedEndpoint = metadata.GetMetadata<IAuthorizeData>() is not null || metadata.GetMetadata<AuthorizationPolicy>() is not null;
        if (!protectedEndpoint || metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return null;
        }

        var services = context.RequestServices;
        var policy = await AuthorizationPolicy.CombineAsync(
            services.GetRequiredService<IAuthorizationPolicyProvider>(),
            metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata.GetOrderedMetadata<AuthorizationPolicy>());
        if (policy is null)
        {
            return null;
        }

        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(context.User, context, policy);
        if (result.Succeeded)
        {
            return null;
        }

        return context.User.Identity?.IsAuthenticated == true
            ? Refused(StatusCodes.Status403Forbidden, "tool.forbidden", "The caller is not allowed to use this tool.")
            : Refused(StatusCodes.Status401Unauthorized, "tool.unauthenticated", "The caller is not signed in.");
    }

    private static EndpointToolResponse Refused(int status, string code, string title) =>
        new(status, "application/problem+json", JsonSerializer.Serialize(new { title, status, code }));

    internal static bool CarriesBody(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is [var method, ..] && CarriesBody(method);

    private static bool CarriesBody(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method);

    private static string AsText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    private static string[] AsQueryValues(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(AsText).ToArray() : [AsText(value)];

    // routing already happened; this is for logs and anything that reads Request.Path
    private static string BuildPath(RoutePattern pattern, RouteValueDictionary values)
    {
        var path = new StringBuilder();
        foreach (var segment in pattern.PathSegments)
        {
            path.Append('/');
            foreach (var part in segment.Parts)
            {
                path.Append(part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternSeparatorPart separator => separator.Content,
                    RoutePatternParameterPart parameter => Uri.EscapeDataString(values[parameter.Name]?.ToString() ?? string.Empty),
                    _ => string.Empty,
                });
            }
        }

        return path.Length == 0 ? "/" : path.ToString();
    }

    private sealed class HasBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
