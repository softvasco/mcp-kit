using System.Text.RegularExpressions;

namespace McpKit.OpenApi;

/// <summary>Which operations become tools and what they are called; binds from config.</summary>
public sealed partial class OpenApiToolSelection
{
    /// <summary>When not empty, only these tools are served.</summary>
    public IList<string> Include { get; } = [];

    public IList<string> Exclude { get; } = [];

    /// <summary>Generated name to the name the model sees.</summary>
    public IDictionary<string, string> Rename { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    // a typo in config would otherwise serve the wrong set of tools without a word
    public IReadOnlyList<OpenApiTool> Apply(IReadOnlyList<OpenApiTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var known = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = Include.Concat(Exclude).Concat(Rename.Keys).Where(n => !known.Contains(n)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"The OpenAPI document has no operation named {string.Join(", ", unknown.Select(n => $"'{n}'"))}.");
        }

        var invalid = Rename.Values.Where(n => !ToolName().IsMatch(n)).ToList();
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Tool names use letters, digits, '_', '-' and '.', up to 128 characters: {string.Join(", ", invalid.Select(n => $"'{n}'"))}.");
        }

        var selected = tools
            .Where(t => Include.Count == 0 || Include.Contains(t.Name, StringComparer.Ordinal))
            .Where(t => !Exclude.Contains(t.Name, StringComparer.Ordinal))
            .Select(t => Rename.TryGetValue(t.Name, out var name) ? t with { Name = name } : t)
            .ToList();

        var duplicate = selected.GroupBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Tool name '{duplicate.Key}' is used by {string.Join(" and ", duplicate.Select(t => $"{t.Method} {t.PathTemplate}"))}.");
        }

        return selected;
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,128}$")]
    private static partial Regex ToolName();
}
