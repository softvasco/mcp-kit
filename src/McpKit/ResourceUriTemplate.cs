using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.WebUtilities;

namespace McpKit;

/// <summary>A resource URI from a route: route values in the path, the rest in the query.</summary>
public sealed class ResourceUriTemplate
{
    private readonly string _prefix;
    private readonly IReadOnlyList<RoutePatternPart> _segments;

    private ResourceUriTemplate(string scheme, IReadOnlyList<RoutePatternPart> segments, IReadOnlyList<string> query)
    {
        _prefix = scheme + "://";
        _segments = segments;
        Query = query;

        var path = string.Join('/', segments.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternParameterPart parameter => "{" + parameter.Name + "}",
            _ => throw new UnreachableException(),
        }));
        Template = query.Count == 0 ? _prefix + path : $"{_prefix}{path}{{?{string.Join(',', query)}}}";
    }

    /// <summary>The RFC 6570 template, like api://accounts/{id}/statement{?page}.</summary>
    public string Template { get; }

    public IReadOnlyList<string> Query { get; }

    public static ResourceUriTemplate For(RouteEndpoint endpoint, string scheme, IEnumerable<string> query)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentNullException.ThrowIfNull(query);

        var route = endpoint.RoutePattern;

        // a URI can't mark a part as optional or as the rest of the path, so segments stay simple
        var segments = route.PathSegments
            .Select(segment => segment.Parts is [RoutePatternLiteralPart or RoutePatternParameterPart { IsOptional: false, IsCatchAll: false }]
                ? segment.Parts[0]
                : throw new InvalidOperationException(
                    $"Endpoint '{route.RawText}' can't be a resource: each route segment must be plain text or one required parameter."))
            .ToList();

        return new ResourceUriTemplate(scheme, segments, [.. query]);
    }

    /// <summary>Route and query values from a URI with this path, or null otherwise.</summary>
    public ResourceUriMatch? Match(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = uri[_prefix.Length..];
        var queryStart = rest.IndexOf('?', StringComparison.Ordinal);
        var parts = (queryStart < 0 ? rest : rest[..queryStart]).Split('/');
        if (parts.Length != _segments.Count)
        {
            return null;
        }

        var arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parts.Length; i++)
        {
            switch (_segments[i])
            {
                case RoutePatternLiteralPart literal when !literal.Content.Equals(parts[i], StringComparison.OrdinalIgnoreCase):
                    return null;
                case RoutePatternParameterPart parameter:
                    var value = Uri.UnescapeDataString(parts[i]);
                    if (value.Length == 0)
                    {
                        return null;
                    }

                    arguments[parameter.Name] = JsonSerializer.SerializeToElement(value);
                    break;
            }
        }

        var unknown = new List<string>();
        if (queryStart >= 0)
        {
            foreach (var (name, values) in QueryHelpers.ParseQuery(rest[queryStart..]))
            {
                if (!Query.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    unknown.Add(name);
                    continue;
                }

                arguments[name] = values.Count == 1
                    ? JsonSerializer.SerializeToElement(values[0])
                    : JsonSerializer.SerializeToElement(values.ToArray());
            }
        }

        return new ResourceUriMatch(arguments, unknown);
    }
}

/// <summary>The values a resource URI carried, and query names the endpoint doesn't take.</summary>
public sealed record ResourceUriMatch(IReadOnlyDictionary<string, JsonElement> Arguments, IReadOnlyList<string> UnknownQuery);
