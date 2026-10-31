using System.Text.RegularExpressions;
using InventoryOps.Domain.Common;

namespace InventoryOps.Domain.Catalog;

/// <summary>
/// Stock keeping unit. Normalised to upper case so uniqueness is case-insensitive by construction.
/// </summary>
public sealed partial record Sku
{
    public const int MaxLength = 32;

    public string Value { get; }

    private Sku(string value) => Value = value;

    public static Sku Create(string? raw)
    {
        var normalized = raw?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Pattern().IsMatch(normalized))
        {
            throw new DomainException(
                "catalog.sku_invalid",
                "SKU must be 3-32 characters of letters, digits and dashes, starting and ending with a letter or digit.");
        }

        return new Sku(normalized);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{1,30}[A-Z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
