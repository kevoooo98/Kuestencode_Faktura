using FluentAssertions;
using Kuestencode.Shared.Pdf.Core;
using Xunit;

namespace Kuestencode.Shared.Pdf.Tests.Core;

public class DocumentLineItemTests
{
    [Fact]
    public void TotalNet_OhneRabatt_BerechnetMengeMalEinzelpreis()
    {
        var item = new DocumentLineItem { Description = "Beratung", Quantity = 3, UnitPrice = 50m };

        item.TotalNet.Should().Be(150m);
    }

    [Fact]
    public void TotalNet_MitRabatt_ZiehtRabattProzentualAb()
    {
        var item = new DocumentLineItem { Description = "Beratung", Quantity = 2, UnitPrice = 100m, DiscountPercent = 10m };

        item.TotalNet.Should().Be(180m); // 200 - 10%
    }

    [Fact]
    public void TotalNet_RabattNull_WirdIgnoriert()
    {
        var item = new DocumentLineItem { Description = "Beratung", Quantity = 2, UnitPrice = 100m, DiscountPercent = 0m };

        item.TotalNet.Should().Be(200m);
    }

    [Fact]
    public void VatAmount_BerechnetSichAusTotalNetUndVatRate()
    {
        var item = new DocumentLineItem { Description = "Beratung", Quantity = 1, UnitPrice = 100m, VatRate = 19m };

        item.VatAmount.Should().Be(19m);
    }

    [Fact]
    public void VatAmount_BeruecksichtigtRabattVorSteuer()
    {
        var item = new DocumentLineItem
        {
            Description = "Beratung", Quantity = 1, UnitPrice = 100m, VatRate = 19m, DiscountPercent = 50m
        };

        item.VatAmount.Should().Be(9.5m); // (100 - 50%) * 19%
    }
}
