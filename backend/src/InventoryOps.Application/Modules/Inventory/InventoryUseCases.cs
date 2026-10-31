using FluentValidation;
using InventoryOps.Application.Common;
using InventoryOps.Application.Common.Caching;
using InventoryOps.Application.Common.Persistence;
using InventoryOps.Domain.Inventory;

namespace InventoryOps.Application.Modules.Inventory;

public sealed record AdjustStockCommand(int QuantityChange, AdjustmentReason Reason, string? Note);

public sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.QuantityChange).NotEqual(0).InclusiveBetween(-InventoryItem.MaxQuantity, InventoryItem.MaxQuantity);
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(StockMovement.NoteMaxLength);
        RuleFor(x => x.Note).NotEmpty().When(x => x.Reason == AdjustmentReason.Other)
            .WithMessage("A note is required when the reason is 'Other'.");
    }
}

public sealed record SetReorderThresholdCommand(int ReorderThreshold);

public sealed class SetReorderThresholdValidator : AbstractValidator<SetReorderThresholdCommand>
{
    public SetReorderThresholdValidator() => RuleFor(x => x.ReorderThreshold).InclusiveBetween(0, InventoryItem.MaxQuantity);
}

public sealed class InventoryCommands(
    IInventoryRepository repository,
    IUnitOfWork unitOfWork,
    ICacheStore cache,
    ICurrentUser currentUser,
    IValidator<AdjustStockCommand> adjustValidator,
    IValidator<SetReorderThresholdCommand> thresholdValidator,
    TimeProvider clock)
{
    public async Task AdjustAsync(Guid productId, AdjustStockCommand command, CancellationToken cancellationToken)
    {
        await adjustValidator.ValidateAndThrowAsync(command, cancellationToken);
        var actor = currentUser.RequireUserName();

        await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var item = await repository.GetAsync(productId, ct) ?? throw new NotFoundException("Inventory item", productId);
                item.Adjust(command.QuantityChange, command.Reason, command.Note, actor, clock.GetUtcNow());
            },
            cancellationToken);

        await cache.RemoveAsync(cancellationToken, CacheKeys.DashboardSummary);
    }

    public async Task SetReorderThresholdAsync(Guid productId, SetReorderThresholdCommand command, CancellationToken cancellationToken)
    {
        await thresholdValidator.ValidateAndThrowAsync(command, cancellationToken);

        await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var item = await repository.GetAsync(productId, ct) ?? throw new NotFoundException("Inventory item", productId);
                item.SetReorderThreshold(command.ReorderThreshold, clock.GetUtcNow());
            },
            cancellationToken);

        await cache.RemoveAsync(cancellationToken, CacheKeys.DashboardSummary);
    }
}

public sealed class InventoryListQueryValidator : AbstractValidator<InventoryListQuery>
{
    public InventoryListQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(Paging.MaxSearchLength);
        RuleFor(x => x.SortBy).Must(InventorySortFields.Allowed.Contains)
            .WithMessage($"sortBy must be one of: {string.Join(", ", InventorySortFields.Allowed)}.");
        RuleFor(x => x.SortDirection).IsInEnum();
        this.ApplyPagingRules(x => x.Page, x => x.PageSize);
    }
}

/// <summary>Inventory reads are never cached: availability must always come from SQL Server.</summary>
public sealed class InventoryQueries(IInventoryReadStore readStore, IValidator<InventoryListQuery> validator)
{
    public async Task<PagedResult<InventoryItemDto>> ListAsync(InventoryListQuery query, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(query, cancellationToken);
        return await readStore.ListAsync(query, cancellationToken);
    }

    public async Task<InventoryItemDto> GetAsync(Guid productId, CancellationToken cancellationToken) =>
        await readStore.GetAsync(productId, cancellationToken) ?? throw new NotFoundException("Inventory item", productId);

    public Task<PagedResult<StockMovementDto>> ListMovementsAsync(Guid productId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > Paging.MaxPage || pageSize is < 1 or > Paging.MaxPageSize)
        {
            throw new ValidationException(
                [new FluentValidation.Results.ValidationFailure("pageSize", $"page must be >= 1 and pageSize between 1 and {Paging.MaxPageSize}.")]);
        }

        return readStore.ListMovementsAsync(productId, page, pageSize, cancellationToken);
    }
}
