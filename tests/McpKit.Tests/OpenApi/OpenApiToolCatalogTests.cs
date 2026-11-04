using System.Text;
using System.Text.Json;
using McpKit.OpenApi;

namespace McpKit.Tests.OpenApi;

public class OpenApiToolCatalogTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_operation_becomes_a_tool_named_in_snake_case()
    {
        var tools = await Load();

        Assert.Equal(
            ["add_category", "delete_accounts_id", "deposit", "get_account", "list_accounts", "set_tags"],
            tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_operation_without_an_id_is_named_after_its_verb_and_path()
    {
        var tool = Find(await Load(), "delete_accounts_id");

        Assert.Equal(HttpMethod.Delete, tool.Method);
        Assert.Equal("/accounts/{id}", tool.PathTemplate);
        Assert.Equal("Closes an account.", tool.Description);
    }

    [Fact]
    public async Task Only_operations_that_read_are_marked_as_not_changing_state()
    {
        var tools = await Load();

        Assert.Equal(["get_account", "list_accounts"], tools.Where(t => !t.ChangesState).Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Body_properties_from_a_reference_sit_next_to_the_parameters()
    {
        var tool = Find(await Load(), "deposit");

        Assert.Equal(["Idempotency-Key", "amount", "currency", "id", "reference"], Names(tool.InputSchema.GetProperty("properties")));
        Assert.Equal(["amount", "currency", "id", "Idempotency-Key"], Required(tool.InputSchema));
        Assert.Equal(OpenApiToolBody.Properties, tool.Body);
    }

    [Fact]
    public async Task Limits_from_the_document_reach_the_model_unchanged()
    {
        var properties = Find(await Load(), "deposit").InputSchema.GetProperty("properties");

        Assert.Equal("0.01", properties.GetProperty("amount").GetProperty("minimum").GetRawText());
        Assert.Equal("^[A-Z]{3}$", properties.GetProperty("currency").GetProperty("pattern").GetString());
        Assert.Equal(
            ["null", "string"],
            properties.GetProperty("reference").GetProperty("type").EnumerateArray().Select(t => t.GetString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Query_parameters_keep_their_enum_default_and_description()
    {
        var tool = Find(await Load(), "list_accounts");
        var properties = tool.InputSchema.GetProperty("properties");

        Assert.Equal("""["open","closed"]""", properties.GetProperty("status").GetProperty("enum").GetRawText());
        Assert.Equal(20, properties.GetProperty("limit").GetProperty("default").GetInt32());
        Assert.Equal("Page size.", properties.GetProperty("limit").GetProperty("description").GetString());
        Assert.Empty(Required(tool.InputSchema));
    }

    [Fact]
    public async Task Cookie_parameters_are_left_out()
    {
        var tool = Find(await Load(), "list_accounts");

        Assert.DoesNotContain("session", Names(tool.InputSchema.GetProperty("properties")));
        Assert.All(tool.Parameters, p => Assert.Equal(OpenApiParameterLocation.Query, p.In));
    }

    [Fact]
    public async Task Path_level_parameters_apply_to_each_operation_under_the_path()
    {
        var tool = Find(await Load(), "get_account");

        Assert.Equal([new OpenApiToolParameter("id", OpenApiParameterLocation.Path, Required: true)], tool.Parameters);
        Assert.Equal("uuid", tool.InputSchema.GetProperty("properties").GetProperty("id").GetProperty("format").GetString());
    }

    [Fact]
    public async Task A_body_that_is_not_an_object_is_one_argument_named_body()
    {
        var tool = Find(await Load(), "set_tags");

        Assert.Equal(OpenApiToolBody.Whole, tool.Body);
        Assert.Equal("array", tool.InputSchema.GetProperty("properties").GetProperty("body").GetProperty("type").GetString());
        Assert.Contains("body", Required(tool.InputSchema));
    }

    [Fact]
    public async Task A_recursive_schema_is_cut_off_instead_of_looping()
    {
        var children = Find(await Load(), "add_category").InputSchema.GetProperty("properties").GetProperty("children");

        var depth = 0;
        while (children.TryGetProperty("items", out var items) && items.TryGetProperty("properties", out var properties))
        {
            children = properties.GetProperty("children");
            depth++;
        }

        Assert.InRange(depth, 1, 10);
    }

    [Fact]
    public async Task Two_operations_with_the_same_name_fail_when_the_document_loads()
    {
        const string document = """
            { "openapi": "3.1.1", "info": { "title": "t", "version": "1" }, "paths": {
              "/a": { "get": { "operationId": "getThing" } },
              "/b": { "get": { "operationId": "get_thing" } } } }
            """;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LoadText(document));
        Assert.Contains("get_thing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_document_that_is_not_openapi_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => LoadText("""{ "hello": "world" }"""));
    }

    private static Task<IReadOnlyList<OpenApiTool>> Load() => SampleDocument.LoadToolsAsync(Token);

    private static async Task<IReadOnlyList<OpenApiTool>> LoadText(string json)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await OpenApiToolCatalog.LoadAsync(stream, Token);
    }

    private static OpenApiTool Find(IReadOnlyList<OpenApiTool> tools, string name) => Assert.Single(tools, t => t.Name == name);

    private static List<string> Names(JsonElement properties) =>
        [.. properties.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

    private static List<string> Required(JsonElement schema) =>
        schema.TryGetProperty("required", out var required) ? [.. required.EnumerateArray().Select(r => r.GetString()!)] : [];
}
