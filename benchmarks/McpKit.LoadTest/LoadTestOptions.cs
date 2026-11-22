using System.Globalization;

namespace McpKit.LoadTest;

internal sealed record LoadTestOptions(Uri Url, int[] Clients, int Calls, int Warmup)
{
    public static LoadTestOptions Parse(string[] args)
    {
        string Value(string name, string fallback)
        {
            var at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
        }

        return new LoadTestOptions(
            new Uri(Value("--url", "http://localhost:5199/")),
            [.. Value("--clients", "1,8,32,64").Split(',').Select(c => int.Parse(c, CultureInfo.InvariantCulture))],
            int.Parse(Value("--calls", "500"), CultureInfo.InvariantCulture),
            int.Parse(Value("--warmup", "50"), CultureInfo.InvariantCulture));
    }
}
