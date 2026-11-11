using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpKit;

/// <summary>An MCP prompt that fills a registered template with the client's arguments.</summary>
internal sealed class TemplateMcpServerPrompt(McpPromptTemplate template) : McpServerPrompt
{
    public override Prompt ProtocolPrompt { get; } = new()
    {
        Name = template.Name,
        Description = template.Description,
        Arguments = [.. template.Arguments.Select(a => new PromptArgument { Name = a, Required = true })],
    };

    public override IReadOnlyList<object> Metadata => [];

    public override ValueTask<GetPromptResult> GetAsync(
        RequestContext<GetPromptRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string text;
        try
        {
            text = template.Fill(Values(request.Params?.Arguments));
        }
        catch (McpPromptArgumentException e)
        {
            throw new McpProtocolException(e.Message, McpErrorCode.InvalidParams);
        }

        return ValueTask.FromResult(new GetPromptResult
        {
            Description = template.Description,
            Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = text } }],
        });
    }

    // the spec sends prompt arguments as strings; anything else is a client bug worth naming
    private Dictionary<string, string> Values(IDictionary<string, JsonElement>? arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in arguments ?? new Dictionary<string, JsonElement>())
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw new McpProtocolException($"{template.Name} takes {name} as a string, not {value.ValueKind}.", McpErrorCode.InvalidParams);
            }

            values[name] = value.GetString()!;
        }

        return values;
    }
}
