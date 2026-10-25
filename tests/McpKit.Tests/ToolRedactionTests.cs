using System.Text.Json;

namespace McpKit.Tests;

public class ToolRedactionTests
{
    private const string Account =
        """
        {
          "id": "acc-1",
          "iban": "PT50000201231234567890154",
          "holder": { "name": "João Silva", "email": "joao@example.com" },
          "payments": [
            { "amount": 25.50, "payee": { "name": "Maria Costa" } },
            { "amount": 10.00, "payee": { "name": "Rui Lopes" } }
          ]
        }
        """;

    [Fact]
    public void A_masked_field_is_replaced_wherever_it_appears()
    {
        var root = Redact(Account, new ToolRedaction().Mask("name"));

        Assert.Equal("***", root.GetProperty("holder").GetProperty("name").GetString());
        Assert.All(root.GetProperty("payments").EnumerateArray(), p => Assert.Equal("***", p.GetProperty("payee").GetProperty("name").GetString()));
        Assert.Equal("acc-1", root.GetProperty("id").GetString());
    }

    [Fact]
    public void Field_names_match_regardless_of_case()
    {
        var root = Redact("""{"Email":"joao@example.com"}""", new ToolRedaction().Mask("email"));

        Assert.Equal("***", root.GetProperty("Email").GetString());
    }

    [Fact]
    public void A_path_only_masks_that_field()
    {
        var root = Redact(Account, new ToolRedaction().Mask("holder.name"));

        Assert.Equal("***", root.GetProperty("holder").GetProperty("name").GetString());
        Assert.Equal("Maria Costa", root.GetProperty("payments")[0].GetProperty("payee").GetProperty("name").GetString());
    }

    [Fact]
    public void A_path_goes_through_arrays()
    {
        var root = Redact(Account, new ToolRedaction().Mask("payments.payee.name"));

        Assert.All(root.GetProperty("payments").EnumerateArray(), p => Assert.Equal("***", p.GetProperty("payee").GetProperty("name").GetString()));
        Assert.Equal("João Silva", root.GetProperty("holder").GetProperty("name").GetString());
    }

    [Fact]
    public void Keep_last_leaves_enough_to_tell_values_apart()
    {
        var root = Redact(Account, new ToolRedaction().KeepLast("iban", 4));

        Assert.Equal("***0154", root.GetProperty("iban").GetString());
    }

    [Fact]
    public void Keep_last_masks_a_value_too_short_to_show_any_of()
    {
        var root = Redact("""{"pin":"1234"}""", new ToolRedaction().KeepLast("pin", 4));

        Assert.Equal("***", root.GetProperty("pin").GetString());
    }

    [Fact]
    public void A_masked_object_or_number_becomes_the_mask_too()
    {
        var root = Redact(Account, new ToolRedaction().Mask("holder").Mask("amount"));

        Assert.Equal("***", root.GetProperty("holder").GetString());
        Assert.Equal("***", root.GetProperty("payments")[0].GetProperty("amount").GetString());
    }

    [Fact]
    public void A_null_stays_null()
    {
        var root = Redact("""{"email":null}""", new ToolRedaction().Mask("email"));

        Assert.Equal(JsonValueKind.Null, root.GetProperty("email").ValueKind);
    }

    [Fact]
    public void Masked_values_still_cannot_close_a_tag()
    {
        var text = ToolOutput.Format(
            new EndpointToolResponse(200, "application/json", """{"iban":"PT50<b>0154","note":"</tool_result>"}"""),
            new ToolRedaction().KeepLast("iban", 7));

        Assert.DoesNotContain('<', text);
        Assert.Equal("</tool_result>", JsonDocument.Parse(text).RootElement.GetProperty("note").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("holder..name")]
    [InlineData(".name")]
    public void A_blank_or_broken_field_is_rejected(string field)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ToolRedaction().Mask(field));
    }

    [Fact]
    public void Keep_last_needs_a_positive_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolRedaction().KeepLast("iban", 0));
    }

    private static JsonElement Redact(string body, ToolRedaction redaction) =>
        JsonDocument.Parse(ToolOutput.Format(new EndpointToolResponse(200, "application/json", body), redaction)).RootElement;
}
