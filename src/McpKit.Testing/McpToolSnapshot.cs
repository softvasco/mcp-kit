using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace McpKit.Testing;

/// <summary>
/// Compares what tools/list returns with a file kept in the repo, so a renamed argument or a changed
/// description fails the build until someone looks at it and updates the file.
/// </summary>
public static class McpToolSnapshot
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Every tool the server lists, sorted by name, as indented JSON with \n line endings.</summary>
    public static async Task<string> CaptureAsync(McpClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var entries = tools
            .Select(t => t.ProtocolTool)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => JsonSerializer.SerializeToNode(t, McpJsonUtilities.DefaultOptions))
            .ToArray();

        // \n everywhere, so a snapshot written on Windows matches on Linux CI
        return new JsonArray(entries).ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>
    /// Checks the server's tools against <c>Snapshots/{name}.tools.json</c> next to the calling test file.
    /// On a difference it writes <c>{name}.tools.received.json</c> beside it and throws.
    /// </summary>
    public static async Task MatchAsync(
        McpClient client,
        string name,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string testFile = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(testFile);

        var folder = Path.Join(Path.GetDirectoryName(testFile), "Snapshots");
        Match(await CaptureAsync(client, cancellationToken), Path.Join(folder, $"{name}.tools.json"));
    }

    /// <summary>Compares a captured snapshot with the file at <paramref name="snapshotPath"/>.</summary>
    public static void Match(string actual, string snapshotPath)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

        var received = Path.ChangeExtension(snapshotPath, ".received.json");
        var expected = File.Exists(snapshotPath) ? File.ReadAllText(snapshotPath).ReplaceLineEndings("\n") : null;
        if (expected == actual)
        {
            File.Delete(received);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(received)!);
        File.WriteAllText(received, actual);
        throw new McpToolSnapshotException(expected is null
            ? $"No tool snapshot at {snapshotPath}. Review {received} and rename it to accept it."
            : $"The tools no longer match {snapshotPath}. If the change is intended, review {received} and copy it over the snapshot.");
    }
}
