using FluentValidation;
using InventoryOps.Application.Modules.Activity;
using InventoryOps.Application.Modules.Catalog;
using InventoryOps.Application.Modules.Catalog.Contracts;
using InventoryOps.Application.Modules.Dashboard;
using InventoryOps.Application.Modules.Inventory;
using InventoryOps.Application.Modules.Inventory.Contracts;
using InventoryOps.Application.Modules.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateProductValidator>(ServiceLifetime.Singleton, includeInternalTypes: true);
        services.AddSingleton(TimeProvider.System);

        // Catalog
        services.AddScoped<ICatalogModule, CatalogModule>();
        services.AddScoped<CreateProductHandler>();
        services.AddScoped<UpdateProductHandler>();
        services.AddScoped<SetProductActiveHandler>();
        services.AddScoped<CreateCategoryHandler>();
        services.AddScoped<ProductQueries>();

        // Inventory
        services.AddScoped<InventoryModule>();
        services.AddScoped<IStockReservations>(sp => sp.GetRequiredService<InventoryModule>());
        services.AddScoped<IInventoryProvisioning>(sp => sp.GetRequiredService<InventoryModule>());
        services.AddScoped<InventoryCommands>();
        services.AddScoped<InventoryQueries>();

        // Orders
        services.AddScoped<SubmitOrderHandler>();
        services.AddScoped<OrderWorkflow>();
        services.AddScoped<OrderQueries>();

        // Dashboard & activity
        services.AddScoped<DashboardQueries>();
        services.AddScoped<ActivityQueries>();
        services.AddScoped<ActivityProjector>();

        return services;
    }
}
