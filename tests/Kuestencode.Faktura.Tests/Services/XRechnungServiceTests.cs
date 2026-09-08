using FluentAssertions;
using Kuestencode.Core.Interfaces;
using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Kuestencode.Faktura.Tests.Services;

public class XRechnungServiceTests
{
    private readonly Mock<IInvoiceService> _invoiceService = new();
    private readonly Mock<ICompanyService> _companyService = new();
    private readonly Mock<IPdfGeneratorService> _pdfGeneratorService = new();
    private readonly XRechnungService _service;

    public XRechnungServiceTests()
    {
        _service = new XRechnungService(
            _invoiceService.Object, _companyService.Object, _pdfGeneratorService.Object,
            NullLogger<XRechnungService>.Instance);
    }

    private static Company MakeValidCompany() => new()
    {
        OwnerFullName = "Max Mustermann",
        Address = "Hafenstraße 1",
        PostalCode = "20457",
        City = "Hamburg",
        Country = "Deutschland",
        TaxNumber = "12/345/67890",
        BankAccount = "DE89370400440532013000",
        BankName = "Sparkasse",
        Email = "info@kuestencode.de"
    };

    private static Customer MakeValidCustomer() => new()
    {
        Name = "Nordlicht Media",
        Address = "Speicherstadt 5",
        PostalCode = "20457",
        City = "Hamburg",
        Country = "Deutschland"
    };

    private static Invoice MakeValidInvoice(Customer? customer = null) => new()
    {
        Id = 1,
        InvoiceNumber = "R-2026-0001",
        InvoiceDate = DateTime.UtcNow,
        CustomerId = 1,
        Customer = customer ?? MakeValidCustomer(),
        Items = [new InvoiceItem { Description = "Beratung", Quantity = 1, UnitPrice = 100, VatRate = 19 }]
    };

    private void SetupInvoice(Invoice invoice) =>
        _invoiceService.Setup(s => s.GetByIdAsync(1, true, true)).ReturnsAsync(invoice);

    [Fact]
    public async Task ValidateForXRechnung_VollstaendigeDaten_IstGueltig()
    {
        SetupInvoice(MakeValidInvoice());
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(MakeValidCompany());

        var (isValid, missingFields) = await _service.ValidateForXRechnungAsync(1);

        isValid.Should().BeTrue();
        missingFields.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateForXRechnung_RechnungNichtGefunden_IstUngueltig()
    {
        _invoiceService.Setup(s => s.GetByIdAsync(99, true, true))
            .ReturnsAsync((Invoice?)null);

        var (isValid, missingFields) = await _service.ValidateForXRechnungAsync(99);

        isValid.Should().BeFalse();
        missingFields.Should().Contain("Rechnung nicht gefunden");
    }

    [Fact]
    public async Task ValidateForXRechnung_FirmeninhaberFehlt_MeldetPflichtfeld()
    {
        SetupInvoice(MakeValidInvoice());
        var company = MakeValidCompany();
        company.OwnerFullName = "";
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(company);

        var (isValid, missingFields) = await _service.ValidateForXRechnungAsync(1);

        isValid.Should().BeFalse();
        missingFields.Should().Contain(f => f.Contains("Firmeninhaber"));
    }

    [Fact]
    public async Task ValidateForXRechnung_WederSteuernummerNochUstId_MeldetPflichtfeld()
    {
        SetupInvoice(MakeValidInvoice());
        var company = MakeValidCompany();
        company.TaxNumber = "";
        company.VatId = null;
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(company);

        var (isValid, missingFields) = await _service.ValidateForXRechnungAsync(1);

        isValid.Should().BeFalse();
        missingFields.Should().Contain(f => f.Contains("Steuernummer"));
    }

    [Fact]
    public async Task ValidateForXRechnung_NurUstIdGesetzt_IstAusreichend()
    {
        SetupInvoice(MakeValidInvoice());
        var company = MakeValidCompany();
        company.TaxNumber = "";
        company.VatId = "DE123456789";
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(company);

        var (isValid, _) = await _service.ValidateForXRechnungAsync(1);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateForXRechnung_WederEmailNochEndpointId_MeldetBT34Pflichtfeld()
    {
        SetupInvoice(MakeValidInvoice());
        var company = MakeValidCompany();
        company.Email = "";
        company.EndpointId = null;
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(company);

        var (isValid, missingFields) = await _service.ValidateForXRechnungAsync(1);

        isValid.Should().BeFalse();
        missingFields.Should().Contain(f => f.Contains("BT-34"));
    }

    [Fact]
    public async Task ValidateForXRechnung_NurEndpointIdGesetzt_IstAusreichend()
    {
        SetupInvoice(MakeValidInvoice());
        var company = MakeValidCompany();
        company.Email = "";
        company.EndpointId = "9930:DE123456789";
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(company);

        var (isValid, _) = await _service.ValidateForXRechnungAsync(1);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateForXRechnung_KundeNichtZugeordnet_MeldetPflichtfeld()
    {
        var invoice = MakeValidInvoice();
        invoice.Customer = null;
        SetupInvoice(invoice);
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(MakeValidCompany());

        var (isValid, missingFields) = await _service.ValidateForXRechnungAsync(1);

        isValid.Should().BeFalse();
        missingFields.Should().Contain("Kunde nicht zugeordnet");
    }

    [Theory]
    [InlineData(nameof(Customer.Name), "Kundenname")]
    [InlineData(nameof(Customer.Address), "Kundenadresse")]
    [InlineData(nameof(Customer.PostalCode), "Kunden PLZ")]
    [InlineData(nameof(Customer.City), "Kunden Stadt")]
    [InlineData(nameof(Customer.Country), "Kunden Land")]
    public async Task ValidateForXRechnung_KundenfeldFehlt_MeldetEntsprechendesPflichtfeld(string property, string expectedMessage)
    {
        var customer = MakeValidCustomer();
        typeof(Customer).GetProperty(property)!.SetValue(customer, "");
        SetupInvoice(MakeValidInvoice(customer));
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(MakeValidCompany());

        var (isValid, missingFields) = await _service.ValidateForXRechnungAsync(1);

        isValid.Should().BeFalse();
        missingFields.Should().Contain(expectedMessage);
    }

    [Fact]
    public async Task ValidateForXRechnung_KeinePositionen_MeldetPflichtfeld()
    {
        var invoice = MakeValidInvoice();
        invoice.Items = [];
        SetupInvoice(invoice);
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(MakeValidCompany());

        var (isValid, missingFields) = await _service.ValidateForXRechnungAsync(1);

        isValid.Should().BeFalse();
        missingFields.Should().Contain("Mindestens eine Rechnungsposition");
    }

    [Fact]
    public async Task ValidateForXRechnung_MehrereFehlendeFelder_MeldetAlle()
    {
        var company = MakeValidCompany();
        company.Address = "";
        company.BankAccount = "";
        SetupInvoice(MakeValidInvoice());
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(company);

        var (isValid, missingFields) = await _service.ValidateForXRechnungAsync(1);

        isValid.Should().BeFalse();
        missingFields.Should().HaveCount(2);
    }
}
