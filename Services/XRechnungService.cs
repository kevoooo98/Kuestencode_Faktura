using InvoiceApp.Models;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Filespec;
using iText.Pdfa;

namespace InvoiceApp.Services;

public class XRechnungService : IXRechnungService
{
    private readonly IInvoiceService _invoiceService;
    private readonly ICompanyService _companyService;
    private readonly IPdfGeneratorService _pdfGeneratorService;
    private readonly ILogger<XRechnungService> _logger;

    // XML Namespaces für ZUGFeRD 2.3 / XRechnung
    private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Udt = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";
    private static readonly XNamespace Qdt = "urn:un:unece:uncefact:data:standard:QualifiedDataType:100";

    public XRechnungService(
        IInvoiceService invoiceService,
        ICompanyService companyService,
        IPdfGeneratorService pdfGeneratorService,
        ILogger<XRechnungService> logger)
    {
        _invoiceService = invoiceService;
        _companyService = companyService;
        _pdfGeneratorService = pdfGeneratorService;
        _logger = logger;
    }

    public async Task<string> GenerateXRechnungXmlAsync(int invoiceId)
    {
        try
        {
            var invoice = await _invoiceService.GetByIdAsync(invoiceId, includeCustomer: true, includeItems: true);
            if (invoice == null)
                throw new ArgumentException($"Invoice {invoiceId} not found");

            var company = await _companyService.GetCompanyAsync();

            // Validierung
            var (isValid, missingFields) = await ValidateForXRechnungAsync(invoiceId);
            if (!isValid)
            {
                throw new InvalidOperationException(
                    $"XRechnung kann nicht generiert werden. Fehlende Pflichtfelder: {string.Join(", ", missingFields)}");
            }

            var xml = BuildXRechnungXml(invoice, company);
            return xml.ToString(SaveOptions.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Generieren der XRechnung für Invoice {InvoiceId}", invoiceId);
            throw;
        }
    }

    public async Task<byte[]> GenerateZugferdPdfAsync(int invoiceId)
    {
        try
        {
            // Generiere normales PDF
            var basePdf = _pdfGeneratorService.GenerateInvoicePdf(invoiceId);

            // Generiere XML
            var xmlString = await GenerateXRechnungXmlAsync(invoiceId);
            var xmlBytes = Encoding.UTF8.GetBytes(xmlString);

            // Erstelle ZUGFeRD-PDF mit eingebettetem XML
            using var outputStream = new MemoryStream();
            using var inputStream = new MemoryStream(basePdf);

            // Öffne PDF und füge XML als Attachment hinzu
            using (var pdfReader = new PdfReader(inputStream))
            using (var pdfWriter = new PdfWriter(outputStream))
            using (var pdfDocument = new PdfDocument(pdfReader, pdfWriter))
            {
                // Erstelle File Specification für das eingebettete XML
                var fileSpec = PdfFileSpec.CreateEmbeddedFileSpec(
                    pdfDocument,
                    xmlBytes,
                    "factur-x.xml",  // ZUGFeRD/Factur-X Standard-Dateiname
                    "factur-x.xml",
                    new PdfName("text/xml"),
                    null,
                    new PdfName("Alternative")  // AFRelationship für PDF/A-3
                );

                // Füge das XML als Embedded File hinzu
                pdfDocument.AddFileAttachment("factur-x.xml", fileSpec);

                // Setze PDF/A-3 Metadata (optional, für volle Konformität)
                // Setze PDF/A-3 / Associated Files (AF muss ein Array sein)
                var catalog = pdfDocument.GetCatalog();
                var afArray = new iText.Kernel.Pdf.PdfArray();
                afArray.Add(fileSpec.GetPdfObject());
                catalog.Put(new PdfName("AF"), afArray);

            }

            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Generieren des ZUGFeRD-PDFs für Invoice {InvoiceId}", invoiceId);
            throw;
        }
    }

    public async Task<(bool IsValid, List<string> MissingFields)> ValidateForXRechnungAsync(int invoiceId)
    {
        var missingFields = new List<string>();

        try
        {
            var invoice = await _invoiceService.GetByIdAsync(invoiceId, includeCustomer: true, includeItems: true);
            if (invoice == null)
            {
                missingFields.Add("Rechnung nicht gefunden");
                return (false, missingFields);
            }

            var company = await _companyService.GetCompanyAsync();

            // Firmendaten validieren
            if (string.IsNullOrWhiteSpace(company.OwnerFullName))
                missingFields.Add("Firmeninhaber Name (Pflichtfeld)");

            if (string.IsNullOrWhiteSpace(company.Address))
                missingFields.Add("Firmenadresse");

            if (string.IsNullOrWhiteSpace(company.PostalCode))
                missingFields.Add("Firmen PLZ");

            if (string.IsNullOrWhiteSpace(company.City))
                missingFields.Add("Firmen Stadt");

            if (string.IsNullOrWhiteSpace(company.Country))
                missingFields.Add("Firmen Land");

            if (string.IsNullOrWhiteSpace(company.TaxNumber))
                missingFields.Add("Steuernummer");

            if (string.IsNullOrWhiteSpace(company.BankAccount))
                missingFields.Add("IBAN");

            if (string.IsNullOrWhiteSpace(company.BankName))
                missingFields.Add("Bankname");

            // BT-34 Seller electronic address ist PFLICHT (seit XRechnung 3.0.1 / BR-DE-31)
            // Mindestens Email oder EndpointId muss vorhanden sein
            if (string.IsNullOrWhiteSpace(company.Email) && string.IsNullOrWhiteSpace(company.EndpointId))
                missingFields.Add("Firmen Email oder Endpoint-ID (mind. eines erforderlich für BT-34 elektronische Adresse)");

            // Kundendaten validieren
            if (invoice.Customer == null)
            {
                missingFields.Add("Kunde nicht zugeordnet");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(invoice.Customer.Name))
                    missingFields.Add("Kundenname");

                if (string.IsNullOrWhiteSpace(invoice.Customer.Address))
                    missingFields.Add("Kundenadresse");

                if (string.IsNullOrWhiteSpace(invoice.Customer.PostalCode))
                    missingFields.Add("Kunden PLZ");

                if (string.IsNullOrWhiteSpace(invoice.Customer.City))
                    missingFields.Add("Kunden Stadt");

                if (string.IsNullOrWhiteSpace(invoice.Customer.Country))
                    missingFields.Add("Kunden Land");
            }

            // Rechnungspositionen validieren
            if (invoice.Items == null || !invoice.Items.Any())
                missingFields.Add("Mindestens eine Rechnungsposition");

            return (missingFields.Count == 0, missingFields);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei der Validierung für Invoice {InvoiceId}", invoiceId);
            missingFields.Add($"Validierungsfehler: {ex.Message}");
            return (false, missingFields);
        }
    }

    private XDocument BuildXRechnungXml(Invoice invoice, Company company)
    {
        var culture = CultureInfo.InvariantCulture;

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Rsm + "CrossIndustryInvoice",
                new XAttribute(XNamespace.Xmlns + "rsm", Rsm),
                new XAttribute(XNamespace.Xmlns + "ram", Ram),
                new XAttribute(XNamespace.Xmlns + "udt", Udt),
                new XAttribute(XNamespace.Xmlns + "qdt", Qdt),

                // ExchangedDocumentContext
                new XElement(Rsm + "ExchangedDocumentContext",
                    new XElement(Ram + "BusinessProcessSpecifiedDocumentContextParameter",
                        new XElement(Ram + "ID", "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0")
                    ),
                    new XElement(Ram + "GuidelineSpecifiedDocumentContextParameter",
                        new XElement(Ram + "ID", "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0")
                    )
                ),

                // ExchangedDocument
                new XElement(Rsm + "ExchangedDocument",
                    new XElement(Ram + "ID", invoice.InvoiceNumber),
                    new XElement(Ram + "TypeCode", "380"),
                    new XElement(Ram + "IssueDateTime",
                        new XElement(Udt + "DateTimeString",
                            new XAttribute("format", "102"),
                            invoice.InvoiceDate.ToString("yyyyMMdd")
                        )
                    ),

                    company.IsKleinunternehmer
                        ? new XElement(Ram + "IncludedNote",
                            new XElement(Ram + "Content", "Kleinunternehmerregelung nach § 19 UStG")
                        )
                        : null
                ),

                // SupplyChainTradeTransaction
                new XElement(Rsm + "SupplyChainTradeTransaction",

                    // Line Items
                    BuildLineItems(invoice.Items, company.IsKleinunternehmer),

                    // Header Trade Agreement
                    new XElement(Ram + "ApplicableHeaderTradeAgreement",
                        new XElement(Ram + "BuyerReference", invoice.InvoiceNumber),
                        BuildSellerTradeParty(company),
                        BuildBuyerTradeParty(invoice.Customer!)
                    ),

                    // Header Trade Delivery
                    new XElement(Ram + "ApplicableHeaderTradeDelivery",
                        new XElement(Ram + "ActualDeliverySupplyChainEvent",
                            new XElement(Ram + "OccurrenceDateTime",
                                new XElement(Udt + "DateTimeString",
                                    new XAttribute("format", "102"),
                                    invoice.InvoiceDate.ToString("yyyyMMdd")
                                )
                            )
                        )
                    ),

                    // Header Trade Settlement
                    BuildHeaderTradeSettlement(invoice, company)
                )
            )
        );

        return doc;
    }

    private IEnumerable<XElement> BuildLineItems(ICollection<InvoiceItem> items, bool isKleinunternehmer)
    {
        var culture = CultureInfo.InvariantCulture;
        int lineId = 1;

        foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i.Description)))
        {
            yield return new XElement(Ram + "IncludedSupplyChainTradeLineItem",
                new XElement(Ram + "AssociatedDocumentLineDocument",
                    new XElement(Ram + "LineID", lineId.ToString())
                ),
                new XElement(Ram + "SpecifiedTradeProduct",
                    new XElement(Ram + "Name", item.Description)
                ),
                new XElement(Ram + "SpecifiedLineTradeAgreement",
                    new XElement(Ram + "NetPriceProductTradePrice",
                        new XElement(Ram + "ChargeAmount", item.UnitPrice.ToString("F2", culture))
                    )
                ),
                new XElement(Ram + "SpecifiedLineTradeDelivery",
                    new XElement(Ram + "BilledQuantity",
                        new XAttribute("unitCode", "C62"), // Unit: piece
                        item.Quantity.ToString("F3", culture)
                    )
                ),
                new XElement(Ram + "SpecifiedLineTradeSettlement",
                    new XElement(Ram + "ApplicableTradeTax",
                        new XElement(Ram + "TypeCode", "VAT"),
                        new XElement(Ram + "CategoryCode", isKleinunternehmer ? "E" : "S"),
                        new XElement(Ram + "RateApplicablePercent", isKleinunternehmer ? "0.00" : item.VatRate.ToString("F2", culture))  
                    ),
                    new XElement(Ram + "SpecifiedTradeSettlementLineMonetarySummation",
                        new XElement(Ram + "LineTotalAmount", item.TotalNet.ToString("F2", culture))
                    )
                )
            );

            lineId++;
        }
    }

    private XElement BuildSellerTradeParty(Company company)
    {
        var hasVatId = !string.IsNullOrWhiteSpace(company.VatId);
        var hasPhone = !string.IsNullOrWhiteSpace(company.Phone);
        var hasEmail = !string.IsNullOrWhiteSpace(company.Email);
        
        // BT-29: Seller identifier (optional, nur wenn vorhanden und nicht leer)
        // Unterscheidet sich von BT-34 (electronic address)!
        var hasEndpointForIdentifier = !string.IsNullOrWhiteSpace(company.EndpointId) 
            && !string.IsNullOrWhiteSpace(company.EndpointSchemeId);

        // BT-34: Seller electronic address (PFLICHT seit XRechnung 3.0.1)
        // Priorisierung: 1. EndpointId mit schemeID, 2. Email mit schemeID="EM"
        var hasEndpointForElectronicAddress = !string.IsNullOrWhiteSpace(company.EndpointId);
        var electronicAddressSchemeId = !string.IsNullOrWhiteSpace(company.EndpointSchemeId) 
            ? company.EndpointSchemeId.Trim() 
            : "EM"; // Fallback zu Email
        var electronicAddressValue = hasEndpointForElectronicAddress 
            ? company.EndpointId!.Trim() 
            : company.Email!.Trim(); // Email als Fallback

        // BR-CO-26: Mindestens eins muss vorhanden sein: BT-29, BT-30 oder BT-31
        // Wenn weder Endpoint noch VatId vorhanden → Verwende Steuernummer als Fallback für BT-29
        var needsIdentifierFallback = !hasEndpointForIdentifier && !hasVatId;

        // Liste für alle Child-Elemente (null-Werte werden später gefiltert)
        var elements = new List<XElement?>
        {
            // BT-29: Seller identifier (optional, 0..n)
            // Priorisierung: 1. GlobalID mit schemeID, 2. Steuernummer als einfache ID (Fallback für BR-CO-26)
            hasEndpointForIdentifier
                ? new XElement(Ram + "GlobalID",
                    new XAttribute("schemeID", company.EndpointSchemeId!.Trim()),
                    company.EndpointId!.Trim()
                )
                : (needsIdentifierFallback
                    ? new XElement(Ram + "ID", company.TaxNumber.Trim())
                    : null),

            // BT-27: Seller name (PFLICHT)
            new XElement(Ram + "Name", company.OwnerFullName),

            // BG-6: Seller contact (PFLICHT in XRechnung via BR-DE-2)
            new XElement(Ram + "DefinedTradeContact",
                new XElement(Ram + "PersonName", company.OwnerFullName),

                // BT-42: Seller contact telephone (optional)
                hasPhone
                    ? new XElement(Ram + "TelephoneUniversalCommunication",
                        new XElement(Ram + "CompleteNumber", company.Phone!.Trim())
                    )
                    : null,

                // BT-43: Seller contact email (optional, unterscheidet sich von BT-34!)
                hasEmail
                    ? new XElement(Ram + "EmailURIUniversalCommunication",
                        new XElement(Ram + "URIID",
                            new XAttribute("schemeID", "EM"),
                            company.Email!.Trim()
                        )
                    )
                    : null
            ),

            // BG-5: Seller postal address (PFLICHT)
            new XElement(Ram + "PostalTradeAddress",
                new XElement(Ram + "PostcodeCode", company.PostalCode),
                new XElement(Ram + "LineOne", company.Address),
                new XElement(Ram + "CityName", company.City),
                new XElement(Ram + "CountryID", GetCountryCode(company.Country))
            ),

            // BT-34: Seller electronic address (PFLICHT seit XRechnung 3.0.1 / BR-DE-31)
            // WICHTIG: URIUniversalCommunication, NICHT EndPointURIUniversalCommunication!
            // schemeID ist IMMER verpflichtend (BR-62)
            new XElement(Ram + "URIUniversalCommunication",
                new XElement(Ram + "URIID",
                    new XAttribute("schemeID", electronicAddressSchemeId),
                    electronicAddressValue
                )
            ),

            // BT-32: Seller tax registration (FC = Steuernummer)
            new XElement(Ram + "SpecifiedTaxRegistration",
                new XElement(Ram + "ID",
                    new XAttribute("schemeID", "FC"),
                    company.TaxNumber.Trim()
                )
            ),

            // BT-31: Seller VAT identifier (VA = USt-IdNr., optional)
            hasVatId
                ? new XElement(Ram + "SpecifiedTaxRegistration",
                    new XElement(Ram + "ID",
                        new XAttribute("schemeID", "VA"),
                        company.VatId!.Trim()
                    )
                )
                : null
        };

        // Filtere alle null-Werte raus (wichtig für PEPPOL-EN16931-R008)
        var seller = new XElement(Ram + "SellerTradeParty", 
            elements.Where(e => e != null)
        );

        return seller;
    }

    private XElement BuildBuyerTradeParty(Customer customer)
    {
        var hasEmail = !string.IsNullOrWhiteSpace(customer.Email);

        var elements = new List<XElement?>
        {
            // BT-44: Buyer name (PFLICHT)
            new XElement(Ram + "Name", customer.Name),
            
            // BG-8: Buyer postal address (PFLICHT)
            new XElement(Ram + "PostalTradeAddress",
                new XElement(Ram + "PostcodeCode", customer.PostalCode),
                new XElement(Ram + "LineOne", customer.Address),
                new XElement(Ram + "CityName", customer.City),
                new XElement(Ram + "CountryID", GetCountryCode(customer.Country))
            ),
            
            // BT-49: Buyer electronic address (optional, aber wenn vorhanden mit schemeID)
            hasEmail
                ? new XElement(Ram + "URIUniversalCommunication",
                    new XElement(Ram + "URIID",
                        new XAttribute("schemeID", "EM"),
                        customer.Email!.Trim()
                    )
                )
                : null
        };

        return new XElement(Ram + "BuyerTradeParty", 
            elements.Where(e => e != null)
        );
    }

    private XElement BuildHeaderTradeSettlement(Invoice invoice, Company company)
    {
        var culture = CultureInfo.InvariantCulture;
        var totalNet = invoice.Items.Sum(i => i.TotalNet);
        var totalVat = invoice.Items.Sum(i => i.TotalVat);
        var totalGross = invoice.Items.Sum(i => i.TotalGross);

        var elements = new List<XElement?>
        {
            new XElement(Ram + "InvoiceCurrencyCode", "EUR"),

            // Bankverbindung
            new XElement(Ram + "SpecifiedTradeSettlementPaymentMeans",
                new XElement(Ram + "TypeCode", "58"), // SEPA Credit Transfer
                new XElement(Ram + "Information", invoice.InvoiceNumber),

                new XElement(Ram + "PayeePartyCreditorFinancialAccount",
                    new XElement(Ram + "IBANID", company.BankAccount),
                    !string.IsNullOrWhiteSpace(company.AccountHolder)
                        ? new XElement(Ram + "AccountName", company.AccountHolder)
                        : new XElement(Ram + "AccountName", company.OwnerFullName)
                ),
                new XElement(Ram + "PayeeSpecifiedCreditorFinancialInstitution",
                    new XElement(Ram + "Name", company.BankName)
                )
            ),

            // MwSt-Informationen
            new XElement(Ram + "ApplicableTradeTax",
                new XElement(Ram + "CalculatedAmount", totalVat.ToString("F2", culture)),
                new XElement(Ram + "TypeCode", "VAT"),
                new XElement(Ram + "BasisAmount", totalNet.ToString("F2", culture)),
                new XElement(Ram + "CategoryCode", company.IsKleinunternehmer ? "E" : "S"),
                company.IsKleinunternehmer
                    ? new XElement(Ram + "ExemptionReasonCode", "VATEX-EU-O")
                    : null,
                new XElement(Ram + "RateApplicablePercent",
                    company.IsKleinunternehmer ? "0.00" : "19.00")
            ),

            // Zahlungsbedingungen (optional)
            invoice.DueDate.HasValue
                ? new XElement(Ram + "SpecifiedTradePaymentTerms",
                    new XElement(Ram + "DueDateDateTime",
                        new XElement(Udt + "DateTimeString",
                            new XAttribute("format", "102"),
                            invoice.DueDate.Value.ToString("yyyyMMdd")
                        )
                    )
                  )
                : null,

            // Geldsummen
            new XElement(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation",
                new XElement(Ram + "LineTotalAmount", totalNet.ToString("F2", culture)),
                new XElement(Ram + "ChargeTotalAmount", "0.00"),
                new XElement(Ram + "AllowanceTotalAmount", "0.00"),
                new XElement(Ram + "TaxBasisTotalAmount", totalNet.ToString("F2", culture)),
                new XElement(Ram + "TaxTotalAmount",
                    new XAttribute("currencyID", "EUR"),
                    totalVat.ToString("F2", culture)
                ),
                new XElement(Ram + "GrandTotalAmount", totalGross.ToString("F2", culture)),
                new XElement(Ram + "DuePayableAmount", totalGross.ToString("F2", culture))
            )
        };

        return new XElement(Ram + "ApplicableHeaderTradeSettlement",
            elements.Where(e => e != null)
        );
    }

    private string GetCountryCode(string country)
    {
        // Konvertiere Ländernamen zu ISO 3166-1 Alpha-2 Codes
        return country?.ToUpper() switch
        {
            "DEUTSCHLAND" => "DE",
            "GERMANY" => "DE",
            "ÖSTERREICH" => "AT",
            "AUSTRIA" => "AT",
            "SCHWEIZ" => "CH",
            "SWITZERLAND" => "CH",
            "DE" => "DE",
            "AT" => "AT",
            "CH" => "CH",
            _ => country?.Length == 2 ? country.ToUpper() : "DE" // Fallback
        };
    }
}