using System.Net;
using System.Text;
using System.Text.Json;
using McpKit.OpenApi;

namespace McpKit.Tests.OpenApi;

public sealed class OpenApiToolInvokerTests : IDisposable
{
    private readonly RecordingHandler _upstream = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _upstream.Dispose();

    [Fact]
    public async Task Path_parameters_fill_the_template_under_the_base_address()
    {
        await Invoke("get_account", """{ "id": "acc-1" }""");

        Assert.Equal("GET https://bank.example/api/v1/accounts/acc-1", _upstream.Line);
    }

    [Fact]
    public async Task A_path_value_is_escaped_so_it_cannot_leave_the_template()
    {
        await Invoke("get_account", """{ "id": "../admin?x=1" }""");

        Assert.Equal("GET https://bank.example/api/v1/accounts/..%2Fadmin%3Fx%3D1", _upstream.Line);
    }

    [Fact]
    public async Task Query_parameters_are_sent_only_when_the_model_passes_them()
    {
        await Invoke("list_accounts", """{ "status": "open", "limit": 5 }""");
        Assert.Equal("GET https://bank.example/api/v1/accounts?status=open&limit=5", _upstream.Line);

        await Invoke("list_accounts", "{}");
        Assert.Equal("GET https://bank.example/api/v1/accounts", _upstream.Line);
    }

    [Fact]
    public async Task Body_properties_go_in_the_body_and_parameters_stay_out_of_it()
    {
        await Invoke("deposit", """{ "id": "acc-1", "Idempotency-Key": "k-1", "amount": 25.50, "currency": "EUR" }""");

        Assert.Equal("POST https://bank.example/api/v1/accounts/acc-1/deposits", _upstream.Line);
        Assert.Equal("k-1", _upstream.Header("Idempotency-Key"));
        Assert.Equal("""{"amount":25.50,"currency":"EUR"}""", _upstream.Body);
        Assert.Equal("application/json", _upstream.ContentType);
    }

    [Fact]
    public async Task A_whole_body_argument_is_sent_as_it_is()
    {
        await Invoke("set_tags", """{ "id": "acc-1", "body": ["savings", "joint"] }""");

        Assert.Equal("PUT https://bank.example/api/v1/accounts/acc-1/tags", _upstream.Line);
        Assert.Equal("""["savings", "joint"]""", _upstream.Body);
    }

    [Fact]
    public async Task A_missing_required_parameter_is_answered_without_calling_the_api()
    {
        var response = await Invoke("deposit", """{ "id": "acc-1", "amount": 25, "currency": "EUR" }""");

        Assert.Null(_upstream.Line);
        Assert.Equal(400, response.StatusCode);
        Assert.Equal("tool.argument_missing", JsonElement.Parse(response.Body).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_upstream_status_and_body_come_back_unchanged()
    {
        _upstream.Respond(HttpStatusCode.Conflict, "application/problem+json", """{"code":"account.closed"}""");

        var response = await Invoke("get_account", """{ "id": "acc-1" }""");

        Assert.Equal(409, response.StatusCode);
        Assert.False(response.IsSuccess);
        Assert.Equal("application/problem+json; charset=utf-8", response.ContentType);
        Assert.Equal("""{"code":"account.closed"}""", response.Body);
    }

    private async Task<EndpointToolResponse> Invoke(string toolName, string arguments)
    {
        await using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "OpenApi", "accounts.openapi.json"));
        var tool = Assert.Single(await OpenApiToolCatalog.LoadAsync(file, Token), t => t.Name == toolName);

        using var client = new HttpClient(_upstream, disposeHandler: false) { BaseAddress = new Uri("https://bank.example/api/v1/") };
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments)!;
        return await OpenApiToolInvoker.InvokeAsync(tool, args, client, Token);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _contentType = "application/json";
        private string _body = "{}";
        private HttpRequestMessage? _request;

        public string? Line { get; private set; }

        public string? Body { get; private set; }

        public string? ContentType => _request?.Content?.Headers.ContentType?.MediaType;

        public string? Header(string name) => _request?.Headers.TryGetValues(name, out var values) == true ? values.Single() : null;

        public void Respond(HttpStatusCode status, string contentType, string body) => (_status, _contentType, _body) = (status, contentType, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _request = request;
            Line = $"{request.Method} {request.RequestUri}";
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, _contentType) };
        }
    }
}
