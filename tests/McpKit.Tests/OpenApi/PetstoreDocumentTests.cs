using System.Net;
using System.Text.Json;
using McpKit.OpenApi;

namespace McpKit.Tests.OpenApi;

// the Swagger Petstore (OpenAPI 3.0.4): a document nobody wrote with these tools in mind
public sealed class PetstoreDocumentTests : IDisposable
{
    private readonly Upstream _upstream = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _upstream.Dispose();

    [Fact]
    public async Task All_nineteen_operations_load_as_tools()
    {
        var tools = await SampleDocument.LoadPetstoreAsync(Token);

        Assert.Equal(
            [
                "add_pet", "create_user", "create_users_with_list_input", "delete_order", "delete_pet", "delete_user",
                "find_pets_by_status", "find_pets_by_tags", "get_inventory", "get_order_by_id", "get_pet_by_id",
                "get_user_by_name", "login_user", "logout_user", "place_order", "update_pet", "update_pet_with_form",
                "update_user", "upload_file",
            ],
            tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_pet_body_comes_through_its_reference_with_the_required_fields()
    {
        var schema = (await Tool("add_pet")).InputSchema;

        Assert.Equal(["name", "photoUrls"], schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal("object", schema.GetProperty("properties").GetProperty("category").GetProperty("type").GetString());
        Assert.Equal(
            ["available", "pending", "sold"],
            schema.GetProperty("properties").GetProperty("status").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task An_array_body_is_one_argument()
    {
        var tool = await Tool("create_users_with_list_input");

        Assert.Equal(OpenApiToolBody.Whole, tool.Body);
        Assert.Equal("array", tool.InputSchema.GetProperty("properties").GetProperty("body").GetProperty("type").GetString());
    }

    [Fact]
    public async Task An_integer_path_parameter_is_written_as_a_number()
    {
        await Invoke("get_pet_by_id", """{ "petId": 10 }""");

        Assert.Equal("GET https://petstore.example/api/v3/pet/10", _upstream.Line);
    }

    [Fact]
    public async Task An_array_query_parameter_is_repeated_once_per_value()
    {
        await Invoke("find_pets_by_tags", """{ "tags": ["tag1", "tag 2"] }""");

        Assert.Equal("GET https://petstore.example/api/v3/pet/findByTags?tags=tag1&tags=tag%202", _upstream.Line);
    }

    [Fact]
    public async Task A_header_parameter_goes_in_the_header_not_the_url()
    {
        await Invoke("delete_pet", """{ "petId": 10, "api_key": "k-1" }""");

        Assert.Equal("DELETE https://petstore.example/api/v3/pet/10", _upstream.Line);
        Assert.Equal("k-1", _upstream.Header("api_key"));
    }

    [Fact]
    public async Task Pet_fields_go_in_the_json_body()
    {
        await Invoke("add_pet", """{ "name": "doggie", "photoUrls": [], "status": "available" }""");

        Assert.Equal("POST https://petstore.example/api/v3/pet", _upstream.Line);
        Assert.Equal("""{"name":"doggie","photoUrls":[],"status":"available"}""", _upstream.Body);
    }

    private static async Task<OpenApiTool> Tool(string name) =>
        Assert.Single(await SampleDocument.LoadPetstoreAsync(Token), t => t.Name == name);

    private async Task Invoke(string toolName, string arguments)
    {
        using var client = new HttpClient(_upstream, disposeHandler: false) { BaseAddress = new Uri("https://petstore.example/api/v3/") };
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments)!;
        await OpenApiToolInvoker.InvokeAsync(await Tool(toolName), args, client, Token);
    }

    private sealed class Upstream : HttpMessageHandler
    {
        private HttpRequestMessage? _request;

        public string? Line { get; private set; }

        public string? Body { get; private set; }

        public string? Header(string name) => _request?.Headers.TryGetValues(name, out var values) == true ? values.Single() : null;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _request = request;
            Line = $"{request.Method} {request.RequestUri!.AbsoluteUri}";
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
