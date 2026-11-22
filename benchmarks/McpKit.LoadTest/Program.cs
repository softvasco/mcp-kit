using System.Diagnostics;
using System.Globalization;
using McpKit.LoadTest;
using McpKit.Testing;

// run the ledger sample first: dotnet run -c Release --project samples/McpKit.LedgerSample --urls http://localhost:5199
var options = LoadTestOptions.Parse(args);
var accountId = "0199f1a2-5c3e-7b10-9a4e-2f6d8c1b0e01";
var arguments = new Dictionary<string, object?> { ["id"] = accountId };

Console.WriteLine($"{options.Calls} calls per client against {options.Url}, after {options.Warmup} warm-up calls each");
Console.WriteLine();
Console.WriteLine("| Clients | Path | Calls/s | p50 ms | p95 ms | p99 ms |");
Console.WriteLine("|---:|---|---:|---:|---:|---:|");

using var http = new HttpClient { BaseAddress = options.Url };

foreach (var clients in options.Clients)
{
    var direct = await Run(clients, options, () => Task.FromResult<Func<Task>>(async () =>
    {
        using var response = await http.GetAsync(new Uri($"accounts/{accountId}", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        await response.Content.ReadAsByteArrayAsync();
    }));
    Print(clients, "HTTP GET", direct);

    // one MCP session per client, the way separate agents would connect
    var sessions = new List<IAsyncDisposable>();
    var mcp = await Run(clients, options, async () =>
    {
        var client = await McpTestClient.ConnectAsync(new HttpClient { BaseAddress = options.Url });
        lock (sessions)
        {
            sessions.Add(client);
        }

        return async () => await client.CallToolForJsonAsync("get_account", arguments);
    });
    Print(clients, "MCP get_account", mcp);

    foreach (var session in sessions)
    {
        await session.DisposeAsync();
    }
}

static async Task<Result> Run(int clients, LoadTestOptions options, Func<Task<Func<Task>>> connect)
{
    var calls = await Task.WhenAll(Enumerable.Range(0, clients).Select(_ => connect()));
    await Task.WhenAll(calls.Select(async call =>
    {
        for (var i = 0; i < options.Warmup; i++)
        {
            await call();
        }
    }));

    var total = Stopwatch.StartNew();
    var latencies = await Task.WhenAll(calls.Select(async call =>
    {
        var own = new double[options.Calls];
        for (var i = 0; i < options.Calls; i++)
        {
            var start = Stopwatch.GetTimestamp();
            await call();
            own[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        return own;
    }));
    total.Stop();

    var all = latencies.SelectMany(l => l).Order().ToArray();
    return new Result(all.Length / total.Elapsed.TotalSeconds, Percentile(all, 0.50), Percentile(all, 0.95), Percentile(all, 0.99));
}

static double Percentile(double[] sorted, double p) => sorted[(int)Math.Ceiling(p * sorted.Length) - 1];

static void Print(int clients, string path, Result r) => Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture, $"| {clients} | {path} | {r.CallsPerSecond:F0} | {r.P50:F2} | {r.P95:F2} | {r.P99:F2} |"));

internal sealed record Result(double CallsPerSecond, double P50, double P95, double P99);
