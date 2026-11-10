using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace McpKit.Tests;

public class EndpointResourceCatalogTests
{
    [Fact]
    public void Route_constraints_stay_out_of_the_uri_and_the_query_uses_the_bound_names()
    {
        var resources = Discover(app => app
            .MapGet("/accounts/{id:guid}/statement", (Guid id, [FromQuery(Name = "page_size")] int? size) => id)
            .WithName("GetStatement")
            .AsMcpResource());

        Assert.Equal("api://accounts/{id}/statement{?page_size}", Assert.Single(resources).Uri.Template);
    }

    [Fact]
    public void The_scheme_comes_from_the_options()
    {
        var resources = Discover(app => app.MapGet("/accounts/{id}", (string id) => id).WithName("GetAccount").AsMcpResource(), "ledger");

        Assert.Equal("ledger://accounts/{id}", Assert.Single(resources).Uri.Template);
    }

    [Fact]
    public void A_post_marked_as_a_resource_fails_at_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Discover(app => app.MapPost("/accounts", () => Results.Ok()).WithName("OpenAccount").AsMcpResource()));

        Assert.Contains("isn't a GET", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/files/{*path}")]
    [InlineData("/accounts/{id?}")]
    [InlineData("/reports/{year}-{month}")]
    public void A_route_a_uri_template_cannot_express_fails_at_startup(string route)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Discover(app => app.MapGet(route, () => Results.Ok()).WithName("Read").AsMcpResource()));

        Assert.Contains("can't be a resource", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_resources_with_the_same_uri_fail_at_startup()
    {
        Assert.Throws<InvalidOperationException>(() => Discover(app =>
        {
            app.MapGet("/accounts/{id}", (string id) => id).WithName("GetAccount").AsMcpResource();
            app.MapGet("/Accounts/{key}", (string key) => key).WithName("GetAccountByKey").AsMcpResource("get_account");
        }));
    }

    [Theory]
    [InlineData("api://accounts/acc-1", "acc-1")]
    [InlineData("API://Accounts/acc%201", "acc 1")]
    public void A_uri_with_the_same_path_matches_and_carries_the_route_value(string uri, string id)
    {
        var resource = Assert.Single(Discover(app => app.MapGet("/accounts/{id}", (string id) => id).WithName("GetAccount").AsMcpResource()));

        var match = resource.Uri.Match(uri);

        Assert.NotNull(match);
        Assert.Equal(id, match.Arguments["id"].GetString());
    }

    [Theory]
    [InlineData("other://accounts/acc-1")]
    [InlineData("api://accounts/acc-1/statement")]
    [InlineData("api://accounts/")]
    [InlineData("api://transfers/acc-1")]
    public void Any_other_uri_does_not_match(string uri)
    {
        var resource = Assert.Single(Discover(app => app.MapGet("/accounts/{id}", (string id) => id).WithName("GetAccount").AsMcpResource()));

        Assert.Null(resource.Uri.Match(uri));
    }

    private static IReadOnlyList<EndpointResource> Discover(Action<WebApplication> map, string scheme = "api")
    {
        using var app = WebApplication.CreateBuilder().Build();
        map(app);
        using var endpoints = new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources);
        return new EndpointToolCatalog(endpoints).DiscoverResources(scheme);
    }
}
