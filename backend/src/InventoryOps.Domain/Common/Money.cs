namespace InventoryOps.Domain.Common;

/// <summary>
/// A non-negative monetary amount in a supported ISO 4217 currency. Amounts are stored with
/// two decimal places; values with more precision are rejected rather than silently rounded.
/// </summary>
public sealed record Money
{
    public static readonly IReadOnlySet<string> SupportedCurrencies =
        new HashSet<string>(StringComparer.Ordinal) { "USD", "EUR", "GBP", "TRY" };

    public const decimal MaxAmount = 9_999_999_999.99m;

    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new DomainException("money.negative", "Amount cannot be negative.");
        }

        if (amount > MaxAmount)
        {
            throw new DomainException("money.too_large", "Amount exceeds the supported maximum.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("money.precision", "Amount cannot have more than two decimal places.");
        }

        if (currency is null || !SupportedCurrencies.Contains(currency))
        {
            throw new DomainException("money.currency", $"Currency '{currency}' is not supported.");
        }

        Amount = amount;
        Currency = currency;
    }

    public Money Multiply(int quantity) => new(Amount * quantity, Currency);
}
