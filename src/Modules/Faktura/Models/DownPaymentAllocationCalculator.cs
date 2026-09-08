namespace Kuestencode.Faktura.Models;

public static class DownPaymentAllocationCalculator
{
    /// <summary>
    /// Teilt einen Bruttobetrag anteilig nach dem Netto/Brutto-Verhältnis einer Referenzrechnung auf.
    /// </summary>
    public static (decimal Net, decimal Vat) Split(decimal grossAmount, decimal referenceNet, decimal referenceGross)
    {
        if (referenceGross == 0)
            return (grossAmount, 0);

        var net = Math.Round(grossAmount * (referenceNet / referenceGross), 2, MidpointRounding.AwayFromZero);
        return (net, grossAmount - net);
    }
}
