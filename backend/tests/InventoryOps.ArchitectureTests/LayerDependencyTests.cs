using System.Reflection;
using InventoryOps.Application.Modules.Catalog;
using InventoryOps.Domain.Catalog;
using InventoryOps.Domain.Inventory;
using InventoryOps.Domain.Orders;
using InventoryOps.Infrastructure.Persistence;
using NetArchTest.Rules;
using Shouldly;

namespace InventoryOps.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(Product).Assembly;
    private static readonly Assembly Application = typeof(CreateProductHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(InventoryOpsDbContext).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static void ShouldPass(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful.ShouldBeTrue($"Violations: {string.Join(", ", result.FailingTypeNames ?? [])}");

    [Fact]
    public void Domain_has_no_framework_or_outer_layer_dependencies() =>
        ShouldPass(Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(
            "InventoryOps.Application", "InventoryOps.Infrastructure", "InventoryOps.Api",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "FluentValidation", "RabbitMQ", "StackExchange.Redis").GetResult());

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_delivery_mechanisms() =>
        ShouldPass(Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(
            "InventoryOps.Infrastructure", "InventoryOps.Api",
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "RabbitMQ", "StackExchange.Redis", "Microsoft.Data.SqlClient").GetResult());

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api() =>
        ShouldPass(Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn("InventoryOps.Api").GetResult());

    [Fact]
    public void Endpoints_stay_thin_and_never_touch_persistence_or_aggregates() =>
        ShouldPass(Types.InAssembly(Api).That().ResideInNamespace("InventoryOps.Api.Endpoints")
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "InventoryOps.Infrastructure.Persistence",
                typeof(Product).FullName!, typeof(InventoryItem).FullName!, typeof(Order).FullName!)
            .GetResult());

    [Fact]
    public void Domain_modules_do_not_reference_each_other()
    {
        ShouldPass(Types.InAssembly(Domain).That().ResideInNamespace("InventoryOps.Domain.Orders")
            .ShouldNot().HaveDependencyOnAny("InventoryOps.Domain.Catalog", "InventoryOps.Domain.Inventory").GetResult());
        ShouldPass(Types.InAssembly(Domain).That().ResideInNamespace("InventoryOps.Domain.Inventory")
            .ShouldNot().HaveDependencyOnAny("InventoryOps.Domain.Catalog", "InventoryOps.Domain.Orders").GetResult());
        ShouldPass(Types.InAssembly(Domain).That().ResideInNamespace("InventoryOps.Domain.Catalog")
            .ShouldNot().HaveDependencyOnAny("InventoryOps.Domain.Inventory", "InventoryOps.Domain.Orders").GetResult());
    }

    [Theory]
    [InlineData("Orders", new[] { "Catalog", "Inventory" })]
    [InlineData("Catalog", new[] { "Orders", "Inventory" })]
    [InlineData("Inventory", new[] { "Orders", "Catalog" })]
    public void Application_modules_only_use_other_modules_through_their_contracts(string module, string[] otherModules)
    {
        // Every non-contract type of another module is off limits; its ".Contracts" namespace is allowed.
        var forbidden = otherModules
            .SelectMany(other => Application.GetTypes()
                .Where(t => t.Namespace == $"InventoryOps.Application.Modules.{other}")
                .Select(t => t.FullName!))
            .Concat(otherModules.Select(other => $"InventoryOps.Domain.{other}"))
            .ToArray();

        ShouldPass(Types.InAssembly(Application).That().ResideInNamespace($"InventoryOps.Application.Modules.{module}")
            .And().DoNotResideInNamespace($"InventoryOps.Application.Modules.{module}.Contracts")
            .ShouldNot().HaveDependencyOnAny(forbidden).GetResult());
    }

    [Fact]
    public void Domain_entities_do_not_expose_public_setters()
    {
        var offenders = Domain.GetTypes()
            .Where(t => t.IsClass && t.Namespace?.StartsWith("InventoryOps.Domain", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(p => p.SetMethod?.IsPublic == true && !IsInitOnly(p.SetMethod)).Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
}
