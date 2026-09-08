using FluentAssertions;
using Kuestencode.Core.Models;
using Kuestencode.Shared.Pdf.Components;
using Kuestencode.Shared.Pdf.Core;
using Xunit;

namespace Kuestencode.Shared.Pdf.Tests.Components;

public class PdfTemplateEngineTests
{
    private readonly PdfTemplateEngine _engine = new();

    private static PdfDocumentInfo MakeMetadata() => new()
    {
        DocumentType = "Rechnung",
        DocumentNumber = "R-2026-0001",
        DocumentDate = new DateTime(2026, 3, 1),
        CustomerNumber = "K00001",
        DueDate = new DateTime(2026, 3, 15)
    };

    private static DocumentSummary MakeSummary() => new()
    {
        TotalNet = 100m,
        VatGroups = [new VatGroup { Rate = 19m, Amount = 19m }]
    };

    private static Company MakeCompany() => new() { BusinessName = "Kuestencode GmbH" };
    private static Customer MakeCustomer() => new() { Name = "Nordlicht Media" };

    [Fact]
    public void ReplacePlaceholders_LeererText_GibtTextUnveraendertZurueck()
    {
        var result = _engine.ReplacePlaceholders("", MakeMetadata(), MakeSummary(), MakeCompany(), MakeCustomer());

        result.Should().Be("");
    }

    [Fact]
    public void ReplacePlaceholders_ErsetztAllgemeinePlatzhalter()
    {
        var result = _engine.ReplacePlaceholders(
            "{{Firmenname}} an {{Kundenname}}: {{Dokumentnummer}} vom {{Dokumentdatum}}, fällig {{Faelligkeitsdatum}}",
            MakeMetadata(), MakeSummary(), MakeCompany(), MakeCustomer());

        result.Should().Be("Kuestencode GmbH an Nordlicht Media: R-2026-0001 vom 01.03.2026, fällig 15.03.2026");
    }

    [Fact]
    public void ReplacePlaceholders_RechnungsUndAngebotsPlatzhalter_ZeigenDenselbenWert()
    {
        var summary = MakeSummary();
        var result = _engine.ReplacePlaceholders(
            "{{Rechnungsnummer}}|{{Angebotsnummer}}|{{Rechnungsbetrag}}|{{Angebotsbetrag}}",
            MakeMetadata(), summary, MakeCompany(), MakeCustomer());

        var expectedAmount = summary.TotalGross.ToString("C2", new System.Globalization.CultureInfo("de-DE"));
        result.Should().Be($"R-2026-0001|R-2026-0001|{expectedAmount}|{expectedAmount}");
    }

    [Fact]
    public void ReplacePlaceholders_OhneFaelligkeitsdatum_ErsetztMitLeerstring()
    {
        var metadata = new PdfDocumentInfo
        {
            DocumentType = "Angebot",
            DocumentNumber = "A-2026-0001",
            DocumentDate = new DateTime(2026, 3, 1),
            CustomerNumber = "K00001",
            DueDate = null
        };

        var result = _engine.ReplacePlaceholders("{{Faelligkeitsdatum}}", metadata, MakeSummary(), MakeCompany(), MakeCustomer());

        result.Should().Be("");
    }

    [Fact]
    public void ReplacePlaceholders_BetragsPlatzhalter_NutzenSummaryWerte()
    {
        var summary = MakeSummary();
        var culture = new System.Globalization.CultureInfo("de-DE");

        var result = _engine.ReplacePlaceholders(
            "{{Bruttosumme}}/{{Nettosumme}}/{{ZuZahlen}}", MakeMetadata(), summary, MakeCompany(), MakeCustomer());

        result.Should().Be(
            $"{summary.TotalGross.ToString("C2", culture)}/{summary.TotalNet.ToString("C2", culture)}/{summary.AmountDue.ToString("C2", culture)}");
    }

    [Fact]
    public void ReplacePlaceholders_OhneBusinessName_FaelltAufOwnerFullNameZurueck()
    {
        var company = new Company { OwnerFullName = "Max Mustermann" };

        var result = _engine.ReplacePlaceholders("{{Firmenname}}", MakeMetadata(), MakeSummary(), company, MakeCustomer());

        result.Should().Be("Max Mustermann");
    }

    // ─── ReplaceCompanyPlaceholders ───────────────────────────────────────────

    [Fact]
    public void ReplaceCompanyPlaceholders_LeererText_GibtTextUnveraendertZurueck()
    {
        _engine.ReplaceCompanyPlaceholders("", MakeCompany()).Should().Be("");
    }

    [Fact]
    public void ReplaceCompanyPlaceholders_ErsetztNurFirmenname()
    {
        var result = _engine.ReplaceCompanyPlaceholders("{{Firmenname}} - {{Kundenname}}", MakeCompany());

        result.Should().Be("Kuestencode GmbH - {{Kundenname}}");
    }
}
