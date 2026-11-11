using System.Security.Claims;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpKit;

/// <summary>An MCP prompt that fills a registered template with the client's arguments.</summary>
internal sealed class TemplateMcpServerPrompt : McpServerPrompt
{
    private readonly McpPromptTemplate _template;
    private readonly IReadOnlyList<EndpointResource> _resources;

    public TemplateMcpServerPrompt(McpPromptTemplate template, IReadOnlyList<EndpointResource> resources)
    {
        _template = template;
        _resources = resources;

        // a typo in the URI should stop the host, not fail the first time a user picks the prompt
        if (template.Attach is not null && Find(template.Attach.Replace("{", "", StringComparison.Ordinal).Replace("}", "", StringComparison.Ordinal)) is null)
        {
            throw new InvalidOperationException($"Prompt {template.Name} attaches '{template.Attach}', which no resource matches.");
        }

        ProtocolPrompt = new Prompt
        {
            Name = template.Name,
            Description = template.Description,
            Arguments = [.. template.Arguments.Select(a => new PromptArgument { Name = a, Required = true })],
        };
    }

    public override Prompt ProtocolPrompt { get; }

    public override IReadOnlyList<object> Metadata => [];

    public override async ValueTask<GetPromptResult> GetAsync(
        RequestContext<GetPromptRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var values = Values(request.Params?.Arguments);
        string text;
        string? attach;
        try
        {
            text = _template.Fill(values);
            attach = _template.FillAttach(values);
        }
        catch (McpPromptArgumentException e)
        {
            throw new McpProtocolException(e.Message, McpErrorCode.InvalidParams);
        }

        List<PromptMessage> messages = [new() { Role = Role.User, Content = new TextContentBlock { Text = text } }];
        if (attach is not null)
        {
            // read as the caller, so the prompt can't show what the resource itself would refuse
            var resource = Find(attach) ?? throw new McpProtocolException($"'{attach}' is not a resource URI.", McpErrorCode.ResourceNotFound);
            var services = request.Services ?? throw new InvalidOperationException("The MCP request has no service provider.");
            var user = request.User ?? new ClaimsPrincipal(new ClaimsIdentity());
            var contents = await EndpointMcpServerResource.ReadAsync(resource, attach, services, user, cancellationToken);
            messages.Add(new PromptMessage { Role = Role.User, Content = new EmbeddedResourceBlock { Resource = contents } });
        }

        return new GetPromptResult { Description = _template.Description, Messages = messages };
    }

    private EndpointResource? Find(string uri) => _resources.FirstOrDefault(r => r.Uri.Match(uri) is not null);

    // the spec sends prompt arguments as strings; anything else is a client bug worth naming
    private Dictionary<string, string> Values(IDictionary<string, JsonElement>? arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in arguments ?? new Dictionary<string, JsonElement>())
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new McpProtocolException($"{_template.Name} takes {name} as a string, not {value.ValueKind}.", McpErrorCode.InvalidParams);
            }

            values[name] = value.GetString()!;
        }

        return values;
    }
}
