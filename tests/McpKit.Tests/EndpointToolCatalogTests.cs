using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace McpKit.Tests;

public class EndpointToolCatalogTests
{
    [Fact]
    public void Only_endpoints_marked_as_tools_are_listed()
    {
        var tools = Discover(app =>
        {
            app.MapGet("/accounts/{id}", (Guid id) => id).WithName("GetAccount").AsMcpTool();
            app.MapDelete("/accounts/{id}", (Guid id) => id).WithName("CloseAccount");
        });

        var tool = Assert.Single(tools);
        Assert.Equal("GET", tool.HttpMethod);
        Assert.Equal("/accounts/{id}", tool.RoutePattern);
    }

    [Fact]
    public void The_tool_name_is_the_endpoint_name_in_snake_case()
    {
        var tools = Discover(app => app.MapGet("/accounts/{id}", (Guid id) => id).WithName("GetAccount").AsMcpTool());

        Assert.Equal("get_account", Assert.Single(tools).Name);
    }

    [Fact]
    public void A_name_passed_to_AsMcpTool_wins_over_the_endpoint_name()
    {
        var tools = Discover(app => app.MapGet("/accounts/{id}", (Guid id) => id).WithName("GetAccount").AsMcpTool("read_account"));

        Assert.Equal("read_account", Assert.Single(tools).Name);
    }

    [Fact]
    public void The_summary_becomes_the_description()
    {
        var tools = Discover(app => app.MapGet("/accounts/{id}", (Guid id) => id)
            .WithName("GetAccount")
            .WithSummary("Read an account's balance and status")
            .AsMcpTool());

        Assert.Equal("Read an account's balance and status", Assert.Single(tools).Description);
    }

    [Fact]
    public void Endpoints_in_a_route_group_keep_the_group_prefix()
    {
        var tools = Discover(app =>
        {
            var accounts = app.MapGroup("/accounts");
            accounts.MapPost("/{id}/deposits", (Guid id) => id).WithName("Deposit").AsMcpTool();
        });

        Assert.Equal("/accounts/{id}/deposits", Assert.Single(tools).RoutePattern);
    }

    [Fact]
    public void A_tool_without_a_name_fails_at_discovery()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Discover(app => app.MapGet("/accounts/{id}", (Guid id) => id).AsMcpTool()));

        Assert.Contains("GET /accounts/{id}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_tools_with_the_same_name_fail_at_discovery()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Discover(app =>
        {
            app.MapGet("/accounts/{id}", (Guid id) => id).AsMcpTool("account");
            app.MapGet("/accounts/{id}/summary", (Guid id) => id).AsMcpTool("account");
        }));

        Assert.Contains("'account'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_endpoint_with_several_methods_cannot_be_a_tool()
    {
        Assert.Throws<InvalidOperationException>(() => Discover(app =>
            app.MapMethods("/accounts/{id}", [HttpMethods.Put, HttpMethods.Patch], (Guid id) => id).WithName("Update").AsMcpTool()));
    }

    private static IReadOnlyList<EndpointTool> Discover(Action<WebApplication> map)
    {
        using var app = WebApplication.CreateBuilder().Build();
        map(app);
        var routes = (IEndpointRouteBuilder)app;
        return new EndpointToolCatalog(new CompositeEndpointDataSource(routes.DataSources)).Discover();
    }
}
