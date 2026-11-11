using System.Text.RegularExpressions;

namespace McpKit;

/// <summary>A prompt for the client's user; each {placeholder} is a required argument.</summary>
public sealed partial class McpPromptTemplate
{
    private McpPromptTemplate(string name, string description, string text, string? attach, IReadOnlyList<string> arguments)
    {
        Name = name;
        Description = description;
        Text = text;
        Attach = attach;
        Arguments = arguments;
    }

    public string Name { get; }

    public string Description { get; }

    public string Text { get; }

    /// <summary>A resource sent with the prompt, such as api://accounts/{account_id}.</summary>
    public string? Attach { get; }

    /// <summary>The placeholder names, in the order they first appear.</summary>
    public IReadOnlyList<string> Arguments { get; }

    public static McpPromptTemplate Create(string name, string description, string text, string? attach = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!PromptName().IsMatch(name ?? ""))
        {
            throw new ArgumentException($"'{name}' isn't a prompt name. Use lowercase letters, digits and underscores, starting with a letter.", nameof(name));
        }

        if (attach is not null && !Uri.TryCreate(Placeholder().Replace(attach, "x"), UriKind.Absolute, out _))
        {
            throw new ArgumentException($"'{attach}' isn't a resource URI like api://accounts/{{account_id}}.", nameof(attach));
        }

        var arguments = Placeholder().Matches(text + attach).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
        return new McpPromptTemplate(name!, description, text, attach, arguments);
    }

    /// <summary>The filled text. Fails on a missing or unknown argument.</summary>
    public string Fill(IReadOnlyDictionary<string, string> values)
    {
        Validate(values);
        return Placeholder().Replace(Text, m => values[m.Groups[1].Value]);
    }

    /// <summary>The URI to attach with its values escaped, or null if there is none.</summary>
    public string? FillAttach(IReadOnlyDictionary<string, string> values)
    {
        Validate(values);
        return Attach is null ? null : Placeholder().Replace(Attach, m => Uri.EscapeDataString(values[m.Groups[1].Value]));
    }

    private void Validate(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var unknown = values.Keys.Where(k => !Arguments.Contains(k, StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0)
        {
            throw new McpPromptArgumentException($"{Name} doesn't take {string.Join(", ", unknown)}. It takes {Takes()}.");
        }

        var missing = Arguments.Where(a => string.IsNullOrWhiteSpace(values.GetValueOrDefault(a))).ToArray();
        if (missing.Length > 0)
        {
            throw new McpPromptArgumentException($"{Name} needs {string.Join(", ", missing)}.");
        }
    }

    private string Takes() => Arguments.Count == 0 ? "no arguments" : string.Join(", ", Arguments);

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex PromptName();

    // only {snake_case} counts, so JSON or code samples in the text stay as written
    [GeneratedRegex(@"\{([a-z][a-z0-9_]*)\}")]
    private static partial Regex Placeholder();
}

public sealed class McpPromptArgumentException(string message) : Exception(message);
