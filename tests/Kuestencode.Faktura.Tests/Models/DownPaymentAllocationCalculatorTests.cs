using FluentAssertions;
using Kuestencode.Faktura.Models;
using Xunit;

namespace Kuestencode.Faktura.Tests.Models;

public class DownPaymentAllocationCalculatorTests
{
    [Fact]
    public void Split_NormalfallMit19ProzentMwSt_TeiltBetragProportionalAuf()
    {
        var (net, vat) = DownPaymentAllocationCalculator.Split(grossAmount: 11900m, referenceNet: 10000m, referenceGross: 11900m);

        net.Should().Be(10000m);
        vat.Should().Be(1900m);
    }

    [Fact]
    public void Split_KleinunternehmerQuelle_NettoGleichBruttoKeineMwSt()
    {
        // Netto == Brutto bei Kleinunternehmer/§13b-Rechnungen -> Verhältnis 1, MwSt-Anteil 0
        var (net, vat) = DownPaymentAllocationCalculator.Split(grossAmount: 5000m, referenceNet: 5000m, referenceGross: 5000m);

        net.Should().Be(5000m);
        vat.Should().Be(0m);
    }

    [Fact]
    public void Split_ReferenzBruttoNull_GibtGesamtenBetragAlsNettoZurueck()
    {
        var (net, vat) = DownPaymentAllocationCalculator.Split(grossAmount: 1000m, referenceNet: 0m, referenceGross: 0m);

        net.Should().Be(1000m);
        vat.Should().Be(0m);
    }

    [Fact]
    public void Split_TeilbetragKleinerAlsReferenz_SkaliertVerhaeltnisAnteilig()
    {
        // 3.900,00 € von einer Referenzrechnung mit 10.000,00 €/11.900,00 € (Netto/Brutto)
        var (net, vat) = DownPaymentAllocationCalculator.Split(grossAmount: 3900m, referenceNet: 10000m, referenceGross: 11900m);

        net.Should().Be(3277.31m);
        vat.Should().Be(622.69m);
    }
}
