using FluentAssertions;
using Kuestencode.Shared.Pdf.Core;
using Xunit;

namespace Kuestencode.Shared.Pdf.Tests.Core;

public class DocumentSummaryTests
{
    [Fact]
    public void TotalNetAfterDiscount_OhneRabatt_EntsprichtTotalNet()
    {
        var summary = new DocumentSummary { TotalNet = 200m };

        summary.TotalNetAfterDiscount.Should().Be(200m);
    }

    [Fact]
    public void TotalNetAfterDiscount_MitRabatt_ZiehtRabattAb()
    {
        var summary = new DocumentSummary { TotalNet = 200m, DiscountAmount = 20m };

        summary.TotalNetAfterDiscount.Should().Be(180m);
    }

    [Fact]
    public void TotalVat_SummiertAlleVatGroups()
    {
        var summary = new DocumentSummary
        {
            TotalNet = 200m,
            VatGroups = [new VatGroup { Rate = 19m, Amount = 38m }, new VatGroup { Rate = 7m, Amount = 7m }]
        };

        summary.TotalVat.Should().Be(45m);
    }

    [Fact]
    public void TotalGross_EntsprichtNettoNachRabattPlusMwSt()
    {
        var summary = new DocumentSummary
        {
            TotalNet = 200m,
            DiscountAmount = 20m,
            VatGroups = [new VatGroup { Rate = 19m, Amount = 34.2m }]
        };

        summary.TotalGross.Should().Be(214.2m); // (200-20) + 34.2
    }

    [Fact]
    public void TotalDownPayments_OhneAnzahlungen_GibtNullZurueck()
    {
        var summary = new DocumentSummary { TotalNet = 100m };

        summary.TotalDownPayments.Should().Be(0m);
    }

    [Fact]
    public void TotalDownPayments_SummiertAlleAnzahlungen()
    {
        var summary = new DocumentSummary
        {
            TotalNet = 100m,
            DownPayments =
            [
                new DownPaymentInfo { Description = "Anzahlung 1", Amount = 30m },
                new DownPaymentInfo { Description = "Anzahlung 2", Amount = 20m }
            ]
        };

        summary.TotalDownPayments.Should().Be(50m);
    }

    [Fact]
    public void AmountDue_ZiehtAnzahlungenVonBruttosummeAb()
    {
        var summary = new DocumentSummary
        {
            TotalNet = 200m,
            VatGroups = [new VatGroup { Rate = 19m, Amount = 38m }],
            DownPayments = [new DownPaymentInfo { Description = "Anzahlung", Amount = 100m }]
        };

        summary.AmountDue.Should().Be(138m); // 238 - 100
    }
}
