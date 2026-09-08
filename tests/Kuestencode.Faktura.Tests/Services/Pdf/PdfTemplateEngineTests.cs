using FluentAssertions;
using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services.Pdf;
using Xunit;

namespace Kuestencode.Faktura.Tests.Services.Pdf;

public class PdfTemplateEngineTests
{
    private readonly PdfTemplateEngine _engine = new();

    private static Invoice MakeInvoice() => new()
    {
        InvoiceNumber = "R-2026-0001",
        InvoiceDate = new DateTime(2026, 3, 1),
        DueDate = new DateTime(2026, 3, 15),
        Items = [new InvoiceItem { Quantity = 1, UnitPrice = 100, VatRate = 19 }],
        Customer = new Customer { Name = "Nordlicht Media" }
    };

    [Fact]
    public void ReplacePlaceholders_LeererText_GibtTextUnveraendertZurueck()
    {
        var result = _engine.ReplacePlaceholders("", MakeInvoice(), new Company());
        result.Should().Be("");
    }

    [Fact]
    public void ReplacePlaceholders_ErsetztAlleBekanntenPlatzhalter()
    {
        var company = new Company { BusinessName = "Kuestencode GmbH" };
        var text = "{{Firmenname}} - {{Rechnungsnummer}} vom {{Rechnungsdatum}}, fällig am {{Faelligkeitsdatum}}, " +
                   "Betrag {{Rechnungsbetrag}} für {{Kundenname}}";
        var expectedAmount = 119m.ToString("C2", new System.Globalization.CultureInfo("de-DE"));

        var result = _engine.ReplacePlaceholders(text, MakeInvoice(), company);

        result.Should().Be(
            $"Kuestencode GmbH - R-2026-0001 vom 01.03.2026, fällig am 15.03.2026, " +
            $"Betrag {expectedAmount} für Nordlicht Media");
    }

    [Fact]
    public void ReplacePlaceholders_OhneBusinessName_FaelltAufOwnerFullNameZurueck()
    {
        var company = new Company { OwnerFullName = "Max Mustermann" };

        var result = _engine.ReplacePlaceholders("{{Firmenname}}", MakeInvoice(), company);

        result.Should().Be("Max Mustermann");
    }

    [Fact]
    public void ReplacePlaceholders_OhneFaelligkeitsdatum_ErsetztMitLeerstring()
    {
        var invoice = MakeInvoice();
        invoice.DueDate = null;

        var result = _engine.ReplacePlaceholders("Fällig: {{Faelligkeitsdatum}}", invoice, new Company());

        result.Should().Be("Fällig: ");
    }

    [Fact]
    public void ReplacePlaceholders_OhneKunde_ErsetztMitLeerstring()
    {
        var invoice = MakeInvoice();
        invoice.Customer = null;

        var result = _engine.ReplacePlaceholders("{{Kundenname}}", invoice, new Company());

        result.Should().Be("");
    }
}
