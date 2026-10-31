using FluentValidation;

namespace InventoryOps.Application.Common;

public enum SortDirection
{
    Asc,
    Desc,
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxPage = 10_000;
    public const int MaxSearchLength = 100;

    /// <summary>Shared server-side limits for every paginated query.</summary>
    public static void ApplyPagingRules<T>(this AbstractValidator<T> validator, Func<T, int> page, Func<T, int> pageSize)
    {
        validator.RuleFor(x => page(x)).InclusiveBetween(1, MaxPage).OverridePropertyName("page");
        validator.RuleFor(x => pageSize(x)).InclusiveBetween(1, MaxPageSize).OverridePropertyName("pageSize")
            .WithMessage($"pageSize must be between 1 and {MaxPageSize}.");
    }
}
