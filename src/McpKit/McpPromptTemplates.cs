using System.Collections;

namespace McpKit;

/// <summary>The prompt templates a host registers from code.</summary>
public sealed class McpPromptTemplates : IEnumerable<McpPromptTemplate>
{
    private readonly Dictionary<string, McpPromptTemplate> _templates = new(StringComparer.Ordinal);

    public int Count => _templates.Count;

    public McpPromptTemplates Add(string name, string description, string text, string? attach = null)
    {
        var template = McpPromptTemplate.Create(name, description, text, attach);
        if (!_templates.TryAdd(template.Name, template))
        {
            throw new ArgumentException($"A prompt named '{name}' is already registered.", nameof(name));
        }

        return this;
    }

    public IEnumerator<McpPromptTemplate> GetEnumerator() => _templates.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
