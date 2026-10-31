using InventoryOps.Domain.Catalog;
using InventoryOps.Domain.Common;
using Shouldly;

namespace InventoryOps.UnitTests.Domain;

public sealed class CatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(" elc-usb-01 ", "ELC-USB-01")]
    [InlineData("abc", "ABC")]
    public void Sku_is_trimmed_and_upper_cased(string raw, string expected) => Sku.Create(raw).Value.ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("-ABC")]
    [InlineData("ABC-")]
    [InlineData("AB C")]
    [InlineData("AB_C")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    public void Invalid_skus_are_rejected(string raw) => Should.Throw<DomainException>(() => Sku.Create(raw));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.001)]
    public void Money_rejects_negative_amounts_and_sub_cent_precision(decimal amount) =>
        Should.Throw<DomainException>(() => new Money(amount, "USD"));

    [Fact]
    public void Money_rejects_unsupported_currencies() =>
        Should.Throw<DomainException>(() => new Money(1m, "XYZ")).Code.ShouldBe("money.currency");

    [Fact]
    public void Zero_price_is_allowed() => new Money(0m, "EUR").Amount.ShouldBe(0m);

    [Fact]
    public void Product_creation_validates_and_raises_event()
    {
        var product = Product.Create(Sku.Create("net-01"), "  Switch  ", "  ", Guid.NewGuid(), new Money(99.5m, "USD"), Now);

        product.Sku.ShouldBe("NET-01");
        product.Name.ShouldBe("Switch");
        product.Description.ShouldBeNull();
        product.IsActive.ShouldBeTrue();
        product.DomainEvents.OfType<ProductCreated>().ShouldHaveSingleItem().Sku.ShouldBe("NET-01");
    }

    [Fact]
    public void Product_requires_a_name_and_category()
    {
        Should.Throw<DomainException>(() => Product.Create(Sku.Create("A-1"), " ", null, Guid.NewGuid(), new Money(1m, "USD"), Now));
        Should.Throw<DomainException>(() => Product.Create(Sku.Create("A-1"), "Name", null, Guid.Empty, new Money(1m, "USD"), Now));
    }

    [Fact]
    public void Deactivation_is_idempotent_and_raises_one_event()
    {
        var product = Product.Create(Sku.Create("A-1"), "Name", null, Guid.NewGuid(), new Money(1m, "USD"), Now);
        product.ClearDomainEvents();

        product.Deactivate(Now);
        product.Deactivate(Now);

        product.IsActive.ShouldBeFalse();
        product.DomainEvents.OfType<ProductDeactivated>().ShouldHaveSingleItem();
    }
}
