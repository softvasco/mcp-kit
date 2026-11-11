namespace McpKit.Tests;

public class McpPromptTemplateTests
{
    [Fact]
    public void Each_placeholder_is_an_argument_once_in_the_order_it_first_appears()
    {
        var template = McpPromptTemplate.Create("compare", "Compare two accounts", "Compare {first} with {second}, then {first} again.");

        Assert.Equal(["first", "second"], template.Arguments);
    }

    [Fact]
    public void Braces_that_are_not_snake_case_names_stay_as_text()
    {
        var template = McpPromptTemplate.Create("explain", "Explain a problem", """Explain {"code": "x"} and {Code} for {account_id}.""");

        Assert.Equal(["account_id"], template.Arguments);
        Assert.Equal("""Explain {"code": "x"} and {Code} for acc-1.""", template.Fill(new Dictionary<string, string> { ["account_id"] = "acc-1" }));
    }

    [Fact]
    public void Filling_replaces_every_occurrence()
    {
        var template = McpPromptTemplate.Create("review", "Review an account", "Read {id}. Is anything about {id} unusual?");

        Assert.Equal("Read acc-1. Is anything about acc-1 unusual?", template.Fill(new Dictionary<string, string> { ["id"] = "acc-1" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_missing_or_blank_argument_is_named_in_the_error(string value)
    {
        var template = McpPromptTemplate.Create("compare", "Compare two accounts", "Compare {first} with {second}.");

        var error = Assert.Throws<McpPromptArgumentException>(() => template.Fill(new Dictionary<string, string> { ["first"] = value }));

        Assert.Equal("compare needs first, second.", error.Message);
    }

    [Fact]
    public void An_unknown_argument_is_refused_with_the_ones_it_takes()
    {
        var template = McpPromptTemplate.Create("review", "Review an account", "Review {id}.");

        var error = Assert.Throws<McpPromptArgumentException>(() =>
            template.Fill(new Dictionary<string, string> { ["id"] = "acc-1", ["mode"] = "strict" }));

        Assert.Equal("review doesn't take mode. It takes id.", error.Message);
    }

    [Theory]
    [InlineData("Review")]
    [InlineData("review-account")]
    [InlineData("1review")]
    [InlineData("")]
    public void A_name_that_isnt_snake_case_is_rejected(string name)
    {
        Assert.Throws<ArgumentException>(() => McpPromptTemplate.Create(name, "Review an account", "Review {id}."));
    }

    [Fact]
    public void Two_prompts_with_the_same_name_are_rejected()
    {
        var prompts = new McpPromptTemplates().Add("review", "Review an account", "Review {id}.");

        Assert.Throws<ArgumentException>(() => prompts.Add("review", "Review again", "Review {id} again."));
    }
}
