namespace McpKit.LedgerSample;

// a named entry point, so tests can reference this sample next to the other one
public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton<AccountReadModel>();
        builder.Services.AddMcpKit();

        var app = builder.Build();

        var accounts = app.MapGroup("/accounts");

        accounts.MapGet("/{id:guid}", (Guid id, AccountReadModel model) =>
                model.Find(id) is { } account ? Results.Ok(account) : AccountNotFound(id))
            .WithName("GetAccount")
            .WithSummary("Balance, currency and status of one account")
            .AsMcpTool();

        accounts.MapGet("/{id:guid}/statement", (Guid id, AccountReadModel model, int take = 20) =>
            {
                if (take is < 1 or > 100)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "take must be between 1 and 100",
                        extensions: new Dictionary<string, object?> { ["code"] = "statement.take_out_of_range" });
                }

                return model.Statement(id, take) is { } lines ? Results.Ok(lines) : AccountNotFound(id);
            })
            .WithName("GetStatement")
            .WithSummary("The latest statement lines of one account, newest first")
            .AsMcpTool();

        app.MapMcpKit();
        app.Run();
    }

    private static IResult AccountNotFound(Guid id) => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: $"No account {id}",
        extensions: new Dictionary<string, object?> { ["code"] = "account.not_found" });
}
