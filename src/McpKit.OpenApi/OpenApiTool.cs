using System.Text.Json;

namespace McpKit.OpenApi;

/// <summary>One operation of an OpenAPI document, described as a tool.</summary>
public sealed record OpenApiTool(
    string Name,
    string? Description,
    HttpMethod Method,
    string PathTemplate,
    JsonElement InputSchema,
    IReadOnlyList<OpenApiToolParameter> Parameters,
    OpenApiToolBody Body)
{
    public bool ChangesState => Method != HttpMethod.Get && Method != HttpMethod.Head;
}

public enum OpenApiParameterLocation
{
    Path,
    Query,
    Header,
}

public sealed record OpenApiToolParameter(string Name, OpenApiParameterLocation In, bool Required);

public enum OpenApiToolBody
{
    None,

    /// <summary>A JSON object body; its properties are arguments next to the parameters.</summary>
    Properties,

    /// <summary>Any other JSON body, passed as one argument named <c>body</c>.</summary>
    Whole,
}
