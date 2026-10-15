using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

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

        // middleware is skipped, so nothing would enforce these yet
        if (endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null)
        {
            throw new InvalidOperationException($"Tool '{tool.Name}' requires authorization, which McpKit does not check yet.");
        }

        var handler = endpoint.RequestDelegate
            ?? throw new InvalidOperationException($"Tool '{tool.Name}' has no request delegate.");

        var routeParameters = endpoint.RoutePattern.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = arguments.Where(a => !routeParameters.Contains(a.Key)).ToList();

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

        await using var body = new MemoryStream();
        context.Response.Body = body;

        await handler(context);

        return new EndpointToolResponse(context.Response.StatusCode, context.Response.ContentType, Encoding.UTF8.GetString(body.ToArray()));
    }

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
