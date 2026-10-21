using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpKit.Tests;

public class ToolCallAuditOverMcpTests
{
    private const string AccountId = "acc-4471";

    private static readonly DateTimeOffset Start = new(2026, 10, 21, 20, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly ListAudit _audit = new();
    private int _deposits;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_call_is_recorded_with_its_status_size_and_duration()
    {
        await using var app = await StartHost(_audit);
        await using var client = await Connect(app);

        var result = await client.CallToolAsync("deposit", Deposit(25m), cancellationToken: Token);

        var call = Assert.Single(_audit.Calls);
        Assert.Equal("deposit", call.Tool);
        Assert.Equal(ToolCallOutcome.Succeeded, call.Outcome);
        Assert.Equal(StatusCodes.Status200OK, call.StatusCode);
        Assert.Equal(Encoding.UTF8.GetByteCount(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text), call.ResultBytes);
        Assert.Equal(TimeSpan.FromMilliseconds(250), call.Duration);
        Assert.Equal(Start, call.StartedAt);
    }

    [Fact]
    public async Task The_record_has_a_hash_of_the_arguments_and_not_their_values()
    {
        await using var app = await StartHost(_audit);
        await using var client = await Connect(app);

        await client.CallToolAsync("deposit", Deposit(25m), cancellationToken: Token);

        var call = Assert.Single(_audit.Calls);
        var sent = JsonSerializer.SerializeToElement(Deposit(25m)).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        Assert.Equal(CallFingerprint.Arguments(sent), call.ArgumentsHash);
        Assert.DoesNotContain(AccountId, call.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_call_is_recorded_as_failed_with_the_endpoint_status()
    {
        await using var app = await StartHost(_audit);
        await using var client = await Connect(app);

        await client.CallToolAsync("deposit", Deposit(-5m), cancellationToken: Token);

        var call = Assert.Single(_audit.Calls);
        Assert.Equal(ToolCallOutcome.Failed, call.Outcome);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, call.StatusCode);
    }

    [Fact]
    public async Task A_call_waiting_for_confirmation_is_recorded_and_runs_nothing()
    {
        await using var app = await StartHost(_audit);
        await using var client = await Connect(app);

        await client.CallToolAsync("close_account", new Dictionary<string, object?> { ["id"] = AccountId }, cancellationToken: Token);

        var call = Assert.Single(_audit.Calls);
        Assert.Equal(ToolCallOutcome.ConfirmationRequested, call.Outcome);
        Assert.Null(call.StatusCode);
        Assert.Equal(0, _deposits);
    }

    [Fact]
    public async Task By_default_each_call_is_one_log_entry_without_the_argument_values()
    {
        var logs = new ListLoggerProvider();
        await using var app = await StartHost(audit: null, logs);
        await using var client = await Connect(app);

        await client.CallToolAsync("deposit", Deposit(25m), cancellationToken: Token);

        var entry = Assert.Single(logs.Entries, e => e.Category == "McpKit.LoggerToolCallAudit");
        Assert.StartsWith("Tool deposit called by : Succeeded (200)", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AccountId, entry.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> Deposit(decimal amount) => new() { ["id"] = AccountId, ["amount"] = amount };

    private async Task<WebApplication> StartHost(IToolCallAudit? audit, ListLoggerProvider? logs = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (logs is not null)
        {
            builder.Logging.ClearProviders().AddProvider(logs);
        }

        builder.Services.AddSingleton<TimeProvider>(_clock);
        if (audit is not null)
        {
            builder.Services.AddSingleton(audit);
        }

        builder.Services.AddMcpKit(options => options.AllowWriteTools = true);

        var app = builder.Build();
        app.MapPost("/accounts/{id}/deposits", (string id, DepositRequest deposit) =>
            {
                _clock.Advance(TimeSpan.FromMilliseconds(250));
                Interlocked.Increment(ref _deposits);
                return deposit.Amount > 0
                    ? Results.Ok(new { id, balance = deposit.Amount })
                    : Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity);
            })
            .WithName("Deposit")
            .AsMcpWriteTool();
        app.MapDelete("/accounts/{id}", (string id) => Results.NoContent())
            .WithName("CloseAccount")
            .AsMcpWriteTool()
            .RequireMcpConfirmation();
        app.MapMcpKit();

        await app.StartAsync(Token);
        return app;
    }

    private static async Task<McpClient> Connect(WebApplication app)
    {
        var http = app.GetTestClient();
        var options = new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") };
        return await McpClient.CreateAsync(new HttpClientTransport(options, http, ownsHttpClient: true), cancellationToken: Token);
    }

    private sealed record DepositRequest(decimal Amount);

    private sealed class ListAudit : IToolCallAudit
    {
        public ConcurrentQueue<ToolCallRecord> Calls { get; } = new();

        public ValueTask RecordAsync(ToolCallRecord toolCall, CancellationToken cancellationToken)
        {
            Calls.Enqueue(toolCall);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class ListLogger(string category, ConcurrentQueue<(string, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((category, formatter(state, exception)));
        }
    }
}
