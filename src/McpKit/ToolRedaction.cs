using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpKit;

/// <summary>Fields masked in tool output, by name anywhere or by a dotted path.</summary>
public sealed class ToolRedaction
{
    // fixed width, so the mask doesn't give away how long the value was
    internal const string MaskText = "***";

    private readonly List<FieldRule> _rules = [];

    internal bool IsEmpty => _rules.Count == 0;

    public ToolRedaction Mask(string field) => Add(field, keepLast: 0);

    /// <summary>Masks all but the last characters, so a person can tell two values apart.</summary>
    public ToolRedaction KeepLast(string field, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);
        return Add(field, count);
    }

    internal JsonNode? Apply(JsonNode? root) => IsEmpty ? root : Walk(root, []);

    private ToolRedaction Add(string field, int keepLast)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        var segments = field.Trim().Split('.');
        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"'{field}' is not a field name or a dotted path.", nameof(field));
        }

        _rules.Add(new FieldRule(segments, keepLast));
        return this;
    }

    private JsonNode? Walk(JsonNode? node, List<string> path)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (name, value) in obj)
                {
                    path.Add(name);
                    copy[name] = Match(path) is { } rule ? Masked(value, rule) : Walk(value, path);
                    path.RemoveAt(path.Count - 1);
                }

                return copy;

            // items keep the array's path, so "payments.payee.name" covers every payment
            case JsonArray array:
                return new JsonArray([.. array.Select(item => Walk(item, path))]);

            default:
                return node?.DeepClone();
        }
    }

    private FieldRule? Match(List<string> path) => _rules.LastOrDefault(r => r.Matches(path));

    private static JsonValue? Masked(JsonNode? value, FieldRule rule)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
        return rule.KeepLast > 0 && text.Length > rule.KeepLast
            ? JsonValue.Create(MaskText + text[^rule.KeepLast..])
            : JsonValue.Create(MaskText);
    }

    private sealed record FieldRule(string[] Segments, int KeepLast)
    {
        public bool Matches(List<string> path) => Segments.Length == 1
            ? Same(Segments[0], path[^1])
            : Segments.Length == path.Count && Segments.Zip(path).All(p => Same(p.First, p.Second));

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
