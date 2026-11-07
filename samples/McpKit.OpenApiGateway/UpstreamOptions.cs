namespace McpKit.OpenApiGateway;

/// <summary>The API to serve as tools, bound from the Upstream section.</summary>
public sealed class UpstreamOptions
{
    public const string Section = "Upstream";

    /// <summary>Path to the OpenAPI document, absolute or relative to the content root.</summary>
    public string? Document { get; set; }

    public Uri? BaseAddress { get; set; }

    public bool AllowWriteTools { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Document))
        {
            throw new InvalidOperationException("Set Upstream:Document to the path of the API's OpenAPI document.");
        }

        if (BaseAddress is not { IsAbsoluteUri: true })
        {
            throw new InvalidOperationException("Set Upstream:BaseAddress to the API's absolute base URL.");
        }
    }

    // without the trailing slash, relative paths replace the last segment: .../api/v3 + pet = .../api/pet
    internal Uri ClientBaseAddress() =>
        BaseAddress!.AbsoluteUri.EndsWith('/') ? BaseAddress : new Uri(BaseAddress.AbsoluteUri + "/");
}
