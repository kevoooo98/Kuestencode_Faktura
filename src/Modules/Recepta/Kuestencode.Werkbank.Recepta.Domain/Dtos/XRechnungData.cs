namespace Kuestencode.Werkbank.Recepta.Domain.Dtos;

/// <summary>
/// Strukturierte Daten aus einer XRechnung/ZUGFeRD-Datei.
/// </summary>
public class XRechnungData
{
    // Lieferantendaten
    public string? SupplierName { get; set; }
    public string? SupplierAddress { get; set; }
    public string? SupplierPostalCode { get; set; }
    public string? SupplierCity { get; set; }
    public string? SupplierCountry { get; set; }
    public string? SupplierTaxId { get; set; }
    public string? SupplierIban { get; set; }
    public string? SupplierBic { get; set; }
    public string? SupplierEmail { get; set; }

    // Rechnungsempfänger (Buyer)
    public string? BuyerName { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerPostalCode { get; set; }
    public string? BuyerCity { get; set; }
    public string? BuyerCountry { get; set; }

    // Rechnungsdaten
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    public string? Currency { get; set; }

    // Beträge
    public decimal? AmountNet { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? AmountTax { get; set; }
    public decimal? AmountGross { get; set; }
    public string? TaxExemptionReason { get; set; }

    // Positionsdaten (nur zur Anzeige, nicht persistiert)
    public List<XRechnungLineItem> LineItems { get; set; } = new();
}

/// <summary>
/// Eine Rechnungsposition aus XRechnung/ZUGFeRD.
/// </summary>
public class XRechnungLineItem
{
    public string? Description { get; set; }
    public decimal Quantity { get; set; }
    public string? UnitCode { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxPercent { get; set; }
}
