using System.Text.Json;
using McpKit.LedgerSample;
using McpKit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;

namespace McpKit.Tests;

// every tool, built from its own schema: a model that gets an argument wrong has to be told which one and why
public sealed class ToolArgumentContractTests : IAsyncDisposable
{
    private readonly WebApplicationFactory<AccountReadModel> _host = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Leaving_out_a_required_argument_names_it()
    {
        await using var client = await Connect();

        foreach (var tool in await client.ListToolsAsync(cancellationToken: Token))
        {
            var schema = tool.ProtocolTool.InputSchema;
            foreach (var name in schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()!))
            {
                var arguments = ValidArguments(schema);
                arguments.Remove(name);

                Assert.Equal("is required", await Problem(client, tool.Name, arguments, name));
            }
        }
    }

    [Fact]
    public async Task An_argument_of_the_wrong_type_names_it()
    {
        await using var client = await Connect();

        foreach (var tool in await client.ListToolsAsync(cancellationToken: Token))
        {
            var schema = tool.ProtocolTool.InputSchema;
            foreach (var property in schema.GetProperty("properties").EnumerateObject())
            {
                var arguments = ValidArguments(schema);
                arguments[property.Name] = WrongValue(property.Value);

                Assert.StartsWith("must be", await Problem(client, tool.Name, arguments, property.Name), StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("abc", "must be a UUID")]
    [InlineData(5, "must be a string")]
    public async Task A_bad_account_id_says_what_it_should_be(object id, string problem)
    {
        await using var client = await Connect();

        Assert.Equal(problem, await Problem(client, "get_account", new() { ["id"] = id }, "id"));
    }

    [Fact]
    public async Task A_fraction_is_not_taken_for_a_whole_number()
    {
        await using var client = await Connect();
        var arguments = new Dictionary<string, object?> { ["id"] = AccountReadModel.Lisbon, ["take"] = 1.5 };

        Assert.Equal("must be a whole number", await Problem(client, "get_statement", arguments, "take"));
    }

    [Theory]
    [InlineData("Development", "take")]
    [InlineData("Production", null)]
    public async Task A_value_the_binding_rejects_still_comes_back_as_invalid_arguments(string environment, string? detailMentions)
    {
        var host = _host.WithWebHostBuilder(web => web.UseEnvironment(environment));
        await using var client = await McpTestClient.ConnectAsync(host.CreateClient(), cancellationToken: Token);
        var arguments = new Dictionary<string, object?> { ["id"] = AccountReadModel.Lisbon, ["take"] = 9_999_999_999 };

        var error = await Assert.ThrowsAsync<McpToolCallException>(() => client.CallToolForJsonAsync("get_statement", arguments, Token));

        using var json = JsonDocument.Parse(error.ErrorText!);
        Assert.Equal("tool.invalid_arguments", json.RootElement.GetProperty("code").GetString());
        if (detailMentions is not null)
        {
            Assert.Contains(detailMentions, json.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private async Task<McpClient> Connect() => await McpTestClient.ConnectAsync(_host.CreateClient(), cancellationToken: Token);

    private static async Task<string?> Problem(McpClient client, string tool, Dictionary<string, object?> arguments, string argument)
    {
        var error = await Assert.ThrowsAsync<McpToolCallException>(() => client.CallToolForJsonAsync(tool, arguments, Token));
        using var json = JsonDocument.Parse(error.ErrorText!);
        Assert.Equal("tool.invalid_arguments", json.RootElement.GetProperty("code").GetString());
        return json.RootElement.GetProperty("errors").GetProperty(argument).GetString();
    }

    private static Dictionary<string, object?> ValidArguments(JsonElement schema) =>
        schema.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => ValidValue(p.Value));

    private static object? ValidValue(JsonElement property) => Type(property) switch
    {
        "integer" => 1,
        "number" => 1.5,
        "boolean" => true,
        _ when property.TryGetProperty("format", out var format) && format.GetString() == "uuid" => AccountReadModel.Lisbon,
        _ => "text",
    };

    private static object WrongValue(JsonElement property) => Type(property) == "string" ? 42 : "not a number";

    private static string Type(JsonElement property) =>
        property.GetProperty("type") is { ValueKind: JsonValueKind.Array } types
            ? types.EnumerateArray().Select(t => t.GetString()!).First(t => t != "null")
            : property.GetProperty("type").GetString()!;
}
