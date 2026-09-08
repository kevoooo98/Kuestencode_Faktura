using Kuestencode.Werkbank.Recepta.Domain.Dtos;
using Microsoft.Extensions.Logging;
using s2industries.ZUGFeRD;
using s2industries.ZUGFeRD.PDF;

namespace Kuestencode.Werkbank.Recepta.Services;

/// <summary>
/// Implementierung des XRechnung/ZUGFeRD-Parsers basierend auf ZUGFeRD-csharp.
/// Erkennt und parst XRechnung (UBL 2.1), ZUGFeRD 1.x/2.x und Factur-X.
/// </summary>
public class XRechnungService : IXRechnungService
{
    private readonly ILogger<XRechnungService> _logger;

    public XRechnungService(ILogger<XRechnungService> logger)
    {
        _logger = logger;
    }

    public bool CanProcess(Stream file, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (extension == ".xml")
        {
            return CanProcessXml(file);
        }

        if (extension == ".pdf")
        {
            return CanProcessPdf(file);
        }

        return false;
    }

    public async Task<XRechnungData> ParseAsync(Stream file, string fileName)
    {
        file.Position = 0;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        InvoiceDescriptor descriptor;

        if (extension == ".pdf")
        {
            descriptor = await InvoicePdfProcessor.LoadFromPdfAsync(file);
        }
        else
        {
            descriptor = InvoiceDescriptor.Load(file);
        }

        var data = MapToXRechnungData(descriptor);

        _logger.LogInformation(
            "XRechnung/ZUGFeRD erfolgreich geparst: Rechnung {InvoiceNumber}, Lieferant: {SupplierName}, Brutto: {AmountGross}",
            data.InvoiceNumber, data.SupplierName, data.AmountGross);

        return data;
    }

    /// <summary>
    /// Prüft ob ein XML-Stream ein valides XRechnung/ZUGFeRD-Format ist.
    /// </summary>
    private bool CanProcessXml(Stream file)
    {
        try
        {
            file.Position = 0;
            var descriptor = InvoiceDescriptor.Load(file);
            file.Position = 0;

            // Grundlegende Prüfung: Rechnungsnummer muss vorhanden sein
            return !string.IsNullOrWhiteSpace(descriptor.InvoiceNo);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("XML ist kein gültiges XRechnung/ZUGFeRD-Format: {Message}", ex.Message);
            file.Position = 0;
            return false;
        }
    }

    /// <summary>
    /// Prüft ob ein PDF eingebettetes ZUGFeRD-XML enthält.
    /// </summary>
    private bool CanProcessPdf(Stream file)
    {
        try
        {
            file.Position = 0;
            var descriptor = InvoicePdfProcessor.LoadFromPdf(file);
            file.Position = 0;
            return !string.IsNullOrWhiteSpace(descriptor.InvoiceNo);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("PDF enthält kein ZUGFeRD-XML: {Message}", ex.Message);
            file.Position = 0;
            return false;
        }
    }

    /// <summary>
    /// Mappt einen InvoiceDescriptor auf das interne XRechnungData-DTO.
    /// </summary>
    private XRechnungData MapToXRechnungData(InvoiceDescriptor desc)
    {
        var data = new XRechnungData();

        // Lieferant (Seller)
        if (desc.Seller != null)
        {
            data.SupplierName = desc.Seller.Name;
            data.SupplierAddress = desc.Seller.Street;
            data.SupplierPostalCode = desc.Seller.Postcode;
            data.SupplierCity = desc.Seller.City;
            data.SupplierCountry = desc.Seller.Country?.ToString();
        }

        // USt-ID aus Seller Tax Registrations
        if (desc.SellerTaxRegistration?.Count > 0)
        {
            // Bevorzuge VA (VAT)-Registrierung, fallback auf erste
            var vatReg = desc.SellerTaxRegistration
                .FirstOrDefault(t => t.SchemeID == TaxRegistrationSchemeID.VA)
                ?? desc.SellerTaxRegistration.First();
            data.SupplierTaxId = vatReg.No;
        }

        // Bankverbindung
        if (desc.CreditorBankAccounts?.Count > 0)
        {
            var bank = desc.CreditorBankAccounts.First();
            data.SupplierIban = bank.IBAN;
            data.SupplierBic = bank.BIC;
        }

        // E-Mail aus SellerContact
        if (desc.SellerContact != null)
        {
            data.SupplierEmail = desc.SellerContact.EmailAddress;
        }

        // Rechnungsempfänger (Buyer)
        if (desc.Buyer != null)
        {
            data.BuyerName = desc.Buyer.Name;
            data.BuyerAddress = desc.Buyer.Street;
            data.BuyerPostalCode = desc.Buyer.Postcode;
            data.BuyerCity = desc.Buyer.City;
            data.BuyerCountry = desc.Buyer.Country?.ToString();
        }

        // Rechnungskopf
        data.InvoiceNumber = desc.InvoiceNo;
        data.Currency = desc.Currency.ToString();

        if (desc.InvoiceDate.HasValue)
        {
            data.InvoiceDate = DateOnly.FromDateTime(desc.InvoiceDate.Value);
        }

        // Fälligkeitsdatum aus PaymentTerms
        if (desc.PaymentTerms?.Count > 0)
        {
            var dueDate = desc.PaymentTerms.FirstOrDefault(pt => pt.DueDate.HasValue)?.DueDate;
            if (dueDate.HasValue)
            {
                data.DueDate = DateOnly.FromDateTime(dueDate.Value);
            }
        }

        // Leistungsdatum (Zeitpunkt der Lieferung/Leistung, § 14 Abs. 4 Nr. 6 UStG)
        if (desc.ActualDeliveryDate.HasValue)
        {
            data.DeliveryDate = DateOnly.FromDateTime(desc.ActualDeliveryDate.Value);
        }

        // Beträge
        data.AmountNet = desc.LineTotalAmount ?? desc.TaxBasisAmount;
        data.AmountGross = desc.GrandTotalAmount ?? desc.DuePayableAmount;
        data.AmountTax = desc.TaxTotalAmount;

        // Steuersatz aus erstem Tax-Eintrag, ggf. Hinweis auf Steuerbefreiung (§ 14 Abs. 4 Nr. 8 UStG)
        if (desc.Taxes?.Count > 0)
        {
            var firstTax = desc.Taxes.First();
            data.TaxRate = firstTax.Percent;
            data.TaxExemptionReason = firstTax.ExemptionReason;
        }

        // Positionen
        if (desc.TradeLineItems != null)
        {
            foreach (var line in desc.TradeLineItems)
            {
                data.LineItems.Add(new XRechnungLineItem
                {
                    Description = line.Name,
                    Quantity = line.BilledQuantity,
                    UnitCode = line.UnitCode?.ToString(),
#pragma warning disable CS0618 // ZUGFeRD-csharp 17.x: NetUnitPrice bleibt optional, wird erst ab v18 pflicht (keine Alternative verfügbar)
                    UnitPrice = line.NetUnitPrice ?? 0,
#pragma warning restore CS0618
                    NetAmount = line.LineTotalAmount ?? 0,
                    TaxPercent = line.TaxPercent
                });
            }
        }

        return data;
    }
}
