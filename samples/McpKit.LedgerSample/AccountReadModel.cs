namespace McpKit.LedgerSample;

// same fields as the account summary in ledger-core's read side
public sealed record AccountView(Guid Id, string Iban, string Currency, string Status, decimal Balance);

public sealed record StatementLine(DateOnly BookedOn, string Description, decimal Amount, decimal BalanceAfter);

/// <summary>A read model filled once at startup, so the sample runs without a database.</summary>
public sealed class AccountReadModel
{
    public static readonly Guid Lisbon = Guid.Parse("0199f1a2-5c3e-7b10-9a4e-2f6d8c1b0e01");
    public static readonly Guid Berlin = Guid.Parse("0199f1a2-5c3e-7b10-9a4e-2f6d8c1b0e02");

    private readonly Dictionary<Guid, (AccountView Account, StatementLine[] Lines)> _accounts = new()
    {
        [Lisbon] = (
            new AccountView(Lisbon, "PT50000201231234567890154", "EUR", "Open", 59.50m),
            [
                new StatementLine(new DateOnly(2026, 10, 20), "Opening deposit", 100.00m, 100.00m),
                new StatementLine(new DateOnly(2026, 10, 21), "Card payment", -40.50m, 59.50m),
            ]),
        [Berlin] = (
            new AccountView(Berlin, "DE89370400440532013000", "EUR", "Frozen", 1200.00m),
            [new StatementLine(new DateOnly(2026, 10, 19), "Salary", 1200.00m, 1200.00m)]),
    };

    public AccountView? Find(Guid id) => _accounts.TryGetValue(id, out var entry) ? entry.Account : null;

    /// <summary>Newest first, at most <paramref name="take"/> lines; null when the account doesn't exist.</summary>
    public IReadOnlyList<StatementLine>? Statement(Guid id, int take) =>
        _accounts.TryGetValue(id, out var entry) ? [.. entry.Lines.Reverse().Take(take)] : null;
}
