using McpKit.OpenApi;
using Microsoft.Extensions.Configuration;

namespace McpKit.Tests.OpenApi;

public class OpenApiToolSelectionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_empty_selection_keeps_every_tool()
    {
        var tools = await SampleDocument.LoadToolsAsync(Token);

        Assert.Equal(tools, new OpenApiToolSelection().Apply(tools));
    }

    [Fact]
    public async Task Include_serves_only_the_listed_tools()
    {
        var selection = new OpenApiToolSelection { Include = { "get_account", "deposit" } };

        var served = selection.Apply(await SampleDocument.LoadToolsAsync(Token));

        Assert.Equal(["deposit", "get_account"], Names(served));
    }

    [Fact]
    public async Task Exclude_drops_a_tool_even_when_it_is_included()
    {
        var selection = new OpenApiToolSelection { Include = { "get_account", "deposit" }, Exclude = { "deposit" } };

        var served = selection.Apply(await SampleDocument.LoadToolsAsync(Token));

        Assert.Equal(["get_account"], Names(served));
    }

    [Fact]
    public async Task A_renamed_tool_keeps_its_operation()
    {
        var selection = new OpenApiToolSelection { Rename = { ["delete_accounts_id"] = "close_account" } };

        var served = selection.Apply(await SampleDocument.LoadToolsAsync(Token));

        var closed = Assert.Single(served, t => t.Name == "close_account");
        Assert.Equal(HttpMethod.Delete, closed.Method);
        Assert.Equal("/accounts/{id}", closed.PathTemplate);
        Assert.DoesNotContain("delete_accounts_id", Names(served));
    }

    [Theory]
    [InlineData("Include")]
    [InlineData("Exclude")]
    [InlineData("Rename")]
    public async Task A_name_the_document_does_not_have_fails(string list)
    {
        var selection = new OpenApiToolSelection();
        switch (list)
        {
            case "Include": selection.Include.Add("get_acount"); break;
            case "Exclude": selection.Exclude.Add("get_acount"); break;
            default: selection.Rename["get_acount"] = "find_account"; break;
        }

        var tools = await SampleDocument.LoadToolsAsync(Token);

        var error = Assert.Throws<InvalidOperationException>(() => selection.Apply(tools));
        Assert.Contains("'get_acount'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("close account")]
    [InlineData("")]
    [InlineData("conta/fechar")]
    public async Task A_new_name_a_client_could_reject_fails(string name)
    {
        var selection = new OpenApiToolSelection { Rename = { ["delete_accounts_id"] = name } };
        var tools = await SampleDocument.LoadToolsAsync(Token);

        Assert.Throws<InvalidOperationException>(() => selection.Apply(tools));
    }

    [Fact]
    public async Task Renaming_onto_another_tools_name_fails()
    {
        var selection = new OpenApiToolSelection { Rename = { ["delete_accounts_id"] = "get_account" } };
        var tools = await SampleDocument.LoadToolsAsync(Token);

        var error = Assert.Throws<InvalidOperationException>(() => selection.Apply(tools));
        Assert.Contains("'get_account'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_selection_binds_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bank:Exclude:0"] = "set_tags",
                ["Bank:Exclude:1"] = "add_category",
                ["Bank:Rename:delete_accounts_id"] = "close_account",
            })
            .Build();
        var selection = new OpenApiToolSelection();

        configuration.GetSection("Bank").Bind(selection);
        var served = selection.Apply(await SampleDocument.LoadToolsAsync(Token));

        Assert.Equal(["close_account", "deposit", "get_account", "list_accounts"], Names(served));
    }

    private static List<string> Names(IEnumerable<OpenApiTool> tools) => [.. tools.Select(t => t.Name).Order(StringComparer.Ordinal)];
}
