using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace McpKit.Tests;

public class ToolInputSchemaTests
{
    [Fact]
    public void Body_properties_become_tool_arguments_in_camel_case()
    {
        var schema = SchemaOf(app => app.MapPost("/accounts/{id}/deposits", (string id, Deposit deposit) => id));

        var properties = schema.GetProperty("properties");
        Assert.Equal("number", TypeOf(properties.GetProperty("amount")));
        Assert.True(properties.TryGetProperty("currency", out _));
        Assert.True(properties.TryGetProperty("reference", out _));
    }

    [Fact]
    public void Route_parameters_are_required()
    {
        var schema = SchemaOf(app => app.MapPost("/accounts/{id}/deposits", (string id, Deposit deposit) => id));

        Assert.Contains("id", Required(schema));
    }

    [Fact]
    public void Body_properties_marked_required_stay_required()
    {
        var schema = SchemaOf(app => app.MapPost("/accounts/{id}/deposits", (string id, Deposit deposit) => id));

        Assert.Equal(["amount", "currency", "id"], Required(schema).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_typed_route_parameter_keeps_its_json_type()
    {
        var schema = SchemaOf(app => app.MapGet("/accounts/{id}", (Guid id) => id));

        var id = schema.GetProperty("properties").GetProperty("id");
        Assert.Equal("string", TypeOf(id));
        Assert.Equal("uuid", id.GetProperty("format").GetString());
    }

    [Fact]
    public void An_int_route_parameter_is_an_integer()
    {
        var schema = SchemaOf(app => app.MapGet("/statements/{year}", (int year) => year));

        Assert.Equal("integer", TypeOf(schema.GetProperty("properties").GetProperty("year")));
    }

    [Fact]
    public void A_route_parameter_bound_through_TryParse_is_a_string()
    {
        var schema = SchemaOf(app => app.MapGet("/accounts/{id}", (AccountNumber id) => id.Value));

        Assert.Equal("string", TypeOf(schema.GetProperty("properties").GetProperty("id")));
    }

    [Fact]
    public void An_optional_route_parameter_is_not_required()
    {
        var schema = SchemaOf(app => app.MapGet("/accounts/{id}/entries/{page?}", (string id, int? page) => id));

        Assert.Equal(["id"], Required(schema));
    }

    [Fact]
    public void An_endpoint_without_a_body_lists_only_its_route()
    {
        var schema = SchemaOf(app => app.MapGet("/accounts/{id}", (string id) => id));

        Assert.Equal(["id"], schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Query_parameters_of_a_get_become_arguments()
    {
        var schema = SchemaOf(app => app.MapGet("/accounts/{id}/entries", (string id, int? limit, string? cursor) => id));

        var properties = schema.GetProperty("properties");
        Assert.Equal("integer", TypeOf(properties.GetProperty("limit")));
        Assert.Equal("string", TypeOf(properties.GetProperty("cursor")));
        Assert.Equal(["id"], Required(schema));
    }

    [Fact]
    public void A_query_parameter_without_a_default_is_required()
    {
        var schema = SchemaOf(app => app.MapGet("/statements", (int year, string format = "pdf") => year));

        Assert.Equal(["year"], Required(schema));
        Assert.True(schema.GetProperty("properties").TryGetProperty("format", out _));
    }

    [Fact]
    public void A_renamed_query_parameter_uses_its_query_name()
    {
        var schema = SchemaOf(app => app.MapGet("/entries", ([FromQuery(Name = "page_size")] int? pageSize) => pageSize));

        Assert.Equal(["page_size"], schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Services_headers_and_the_request_itself_are_not_arguments()
    {
        var schema = SchemaOf(app => app.MapGet("/search", (
            string q,
            [FromHeader(Name = "X-Tenant")] string? tenant,
            [FromServices] TimeProvider time,
            HttpContext context,
            CancellationToken cancellationToken) => q));

        Assert.Equal(["q"], schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void A_header_argument_is_an_optional_string_with_its_description()
    {
        var schema = SchemaOf(app => app.MapPost("/accounts/{id}/deposits", (string id, Deposit deposit) => id)
            .WithMcpToolHeader("Idempotency-Key", "idempotency_key", "reuse it on a retry"));

        var key = schema.GetProperty("properties").GetProperty("idempotency_key");
        Assert.Equal("string", TypeOf(key));
        Assert.Equal("reuse it on a retry", key.GetProperty("description").GetString());
        Assert.DoesNotContain("idempotency_key", Required(schema));
    }

    [Fact]
    public void A_header_argument_that_clashes_with_another_argument_fails_at_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => SchemaOf(app => app.MapPost("/accounts/{id}/deposits", (string id, Deposit deposit) => id)
            .WithMcpToolHeader("X-Currency", "currency", "the currency")));

        Assert.Contains("currency", error.Message, StringComparison.Ordinal);
    }

    private static JsonElement SchemaOf(Func<WebApplication, RouteHandlerBuilder> map)
    {
        using var app = WebApplication.CreateBuilder().Build();
        map(app);
        using var endpoints = new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources);
        return ToolInputSchema.For(endpoints.Endpoints.OfType<RouteEndpoint>().Single());
    }

    private static string? TypeOf(JsonElement schema)
    {
        var type = schema.GetProperty("type");
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(t => t.GetString()).Single(t => t != "null")
            : type.GetString();
    }

    private static List<string?> Required(JsonElement schema) =>
        [.. schema.GetProperty("required").EnumerateArray().Select(r => r.GetString())];

    private sealed record Deposit(decimal Amount, string Currency, string? Reference = null);

    private sealed record AccountNumber(string Value) : IParsable<AccountNumber>
    {
        public static AccountNumber Parse(string s, IFormatProvider? provider) => new(s);

        public static bool TryParse(string? s, IFormatProvider? provider, out AccountNumber result)
        {
            result = new AccountNumber(s ?? string.Empty);
            return s is not null;
        }
    }
}
