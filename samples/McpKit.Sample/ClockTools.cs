using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;

namespace McpKit.Sample;

[McpServerToolType]
public sealed class ClockTools(TimeProvider clock)
{
    [McpServerTool(Name = "utc_now")]
    [Description("The server's current time in UTC, ISO 8601.")]
    public string UtcNow() => clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
}
