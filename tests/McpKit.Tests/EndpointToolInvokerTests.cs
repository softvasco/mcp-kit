using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace McpKit.Tests;

public class EndpointToolInvokerTests
{
    private static readonly Guid AccountId = Guid.Parse("5f0e7c38-62a4-4c39-9d43-0b8f6f2b9a10");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Route_values_come_from_the_arguments()
    {
        var response = await Invoke(
            app => app.MapGet("/accounts/{id:guid}", (Guid id) => new { id }).WithName("GetAccount").AsMcpTool(),
            """{ "id": "5f0e7c38-62a4-4c39-9d43-0b8f6f2b9a10" }""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(AccountId, JsonDocument.Parse(response.Body).RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Other_arguments_become_the_query_string_on_a_get()
    {
        var response = await Invoke(
            app => app.MapGet("/accounts/{id:guid}/entries", (Guid id, int take) => new { take }).WithName("ListEntries").AsMcpTool(),
            """{ "id": "5f0e7c38-62a4-4c39-9d43-0b8f6f2b9a10", "take": 20 }""");

        Assert.Equal(20, JsonDocument.Parse(response.Body).RootElement.GetProperty("take").GetInt32());
    }

    [Fact]
    public async Task Other_arguments_become_the_json_body_on_a_post()
    {
        var response = await Invoke(
            app => app.MapPost("/accounts/{id:guid}/deposits", (Guid id, Deposit deposit) => new { id, deposit.Amount, deposit.Currency })
                .WithName("Deposit").AsMcpWriteTool(),
            """{ "id": "5f0e7c38-62a4-4c39-9d43-0b8f6f2b9a10", "amount": 12.50, "currency": "EUR" }""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var body = JsonDocument.Parse(response.Body).RootElement;
        Assert.Equal(12.50m, body.GetProperty("amount").GetDecimal());
        Assert.Equal("EUR", body.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task A_refused_command_comes_back_as_the_same_problem_details()
    {
        var response = await Invoke(
            app => app.MapPost("/accounts/{id:guid}/withdrawals", (Guid id) =>
                    Results.Problem(statusCode: StatusCodes.Status409Conflict, extensions: new Dictionary<string, object?> { ["code"] = "account.insufficient_funds" }))
                .WithName("Withdraw").AsMcpWriteTool(),
            """{ "id": "5f0e7c38-62a4-4c39-9d43-0b8f6f2b9a10" }""");

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.False(response.IsSuccess);
        Assert.StartsWith("application/problem+json", response.ContentType, StringComparison.Ordinal);
        Assert.Equal("account.insufficient_funds", JsonDocument.Parse(response.Body).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Endpoint_filters_run_for_a_tool_call()
    {
        var response = await Invoke(
            app => app.MapPost("/accounts/{id:guid}/deposits", (Guid id, Deposit deposit) => Results.Ok())
                .AddEndpointFilter(async (context, next) =>
                    context.GetArgument<Deposit>(1).Amount > 0 ? await next(context) : Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["must be positive"] }))
                .WithName("Deposit").AsMcpWriteTool(),
            """{ "id": "5f0e7c38-62a4-4c39-9d43-0b8f6f2b9a10", "amount": 0, "currency": "EUR" }""");

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_header_argument_is_sent_as_the_header_and_left_out_of_the_body()
    {
        var response = await Invoke(
            app => app.MapPost("/accounts/{id:guid}/deposits", (Guid id, Deposit deposit, HttpRequest request) =>
                    new { key = request.Headers["Idempotency-Key"].ToString(), deposit.Amount })
                .WithName("Deposit").AsMcpWriteTool()
                .WithMcpToolHeader("Idempotency-Key", "idempotency_key", "reuse it on a retry"),
            """{ "id": "5f0e7c38-62a4-4c39-9d43-0b8f6f2b9a10", "amount": 5, "currency": "EUR", "idempotency_key": "dep-1" }""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var body = JsonDocument.Parse(response.Body).RootElement;
        Assert.Equal("dep-1", body.GetProperty("key").GetString());
        Assert.Equal(5m, body.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task A_header_argument_left_out_sends_no_header()
    {
        var response = await Invoke(
            app => app.MapPost("/accounts/{id:guid}/deposits", (Guid id, Deposit deposit, HttpRequest request) =>
                    new { present = request.Headers.ContainsKey("Idempotency-Key") })
                .WithName("Deposit").AsMcpWriteTool()
                .WithMcpToolHeader("Idempotency-Key", "idempotency_key", "reuse it on a retry"),
            """{ "id": "5f0e7c38-62a4-4c39-9d43-0b8f6f2b9a10", "amount": 5, "currency": "EUR" }""");

        Assert.False(JsonDocument.Parse(response.Body).RootElement.GetProperty("present").GetBoolean());
    }

    [Fact]
    public async Task A_filter_sees_the_header_argument_on_a_get()
    {
        var response = await Invoke(
            app => app.MapGet("/accounts/{id:guid}", (Guid id, int? take) => new { take })
                .AddEndpointFilter(async (context, next) =>
                    context.HttpContext.Request.Headers["X-Tenant"] == "acme" ? await next(context) : Results.Forbid())
                .WithName("GetAccount").AsMcpTool()
                .WithMcpToolHeader("X-Tenant", "tenant", "the tenant to read from"),
            """{ "id": "5f0e7c38-62a4-4c39-9d43-0b8f6f2b9a10", "tenant": "acme" }""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
    }

    [Fact]
    public async Task The_handler_sees_the_caller_as_the_user()
    {
        var caller = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "agent-7")], "test"));

        var response = await Invoke(
            app => app.MapGet("/whoami", (ClaimsPrincipal user) => new { name = user.Identity!.Name }).WithName("WhoAmI").AsMcpTool(),
            "{}",
            caller);

        Assert.Equal("agent-7", JsonDocument.Parse(response.Body).RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_endpoint_that_requires_authorization_is_refused_until_mcp_kit_checks_it()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(
            app => app.MapGet("/accounts/{id:guid}", (Guid id) => id).RequireAuthorization().WithName("GetAccount").AsMcpTool(),
            """{ "id": "5f0e7c38-62a4-4c39-9d43-0b8f6f2b9a10" }"""));

        Assert.Contains("get_account", error.Message, StringComparison.Ordinal);
    }

    private static async Task<EndpointToolResponse> Invoke(Action<WebApplication> map, string arguments, ClaimsPrincipal? user = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        map(app);

        using var endpoints = new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources);
        var tool = Assert.Single(new EndpointToolCatalog(endpoints).Discover());
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments)!;

        await using var scope = app.Services.CreateAsyncScope();
        return await EndpointToolInvoker.InvokeAsync(tool, args, scope.ServiceProvider, user ?? new ClaimsPrincipal(new ClaimsIdentity()), Token);
    }

    private sealed record Deposit(decimal Amount, string Currency);
}
