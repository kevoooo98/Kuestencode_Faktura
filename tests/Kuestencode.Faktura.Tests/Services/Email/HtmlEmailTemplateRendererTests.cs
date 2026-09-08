using FluentAssertions;
using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services.Email;
using Xunit;

namespace Kuestencode.Faktura.Tests.Services.Email;

public class HtmlEmailTemplateRendererTests
{
    private readonly HtmlEmailTemplateRenderer _renderer = new();

    private static Invoice MakeInvoice(
        DiscountType discountType = DiscountType.None,
        decimal? discountValue = null,
        List<DownPayment>? downPayments = null,
        DateTime? dueDate = null) =>
        new()
        {
            InvoiceNumber = "R-2026-0001",
            InvoiceDate = new DateTime(2026, 3, 1),
            DueDate = dueDate,
            DiscountType = discountType,
            DiscountValue = discountValue,
            DownPayments = downPayments ?? [],
            Items = [new InvoiceItem { Quantity = 1, UnitPrice = 100, VatRate = 19 }]
        };

    private static Company MakeCompany(string? bic = null) =>
        new()
        {
            AccountHolder = "Max Mustermann",
            BankName = "Sparkasse",
            BankAccount = "DE89370400440532013000",
            Bic = bic
        };

    // ─── RenderContentHtml ────────────────────────────────────────────────────

    [Fact]
    public void RenderContentHtml_OhneFaelligkeitsdatum_ZeigtSofortFaellig()
    {
        var html = _renderer.RenderContentHtml(MakeInvoice(), MakeCompany());

        html.Should().Contain("Sofort fällig");
    }

    [Fact]
    public void RenderContentHtml_MitFaelligkeitsdatum_ZeigtFormatiertesDatum()
    {
        var html = _renderer.RenderContentHtml(MakeInvoice(dueDate: new DateTime(2026, 3, 15)), MakeCompany());

        html.Should().Contain("15.03.2026");
    }

    [Fact]
    public void RenderContentHtml_OhneRabatt_ZeigtKeinenRabattHinweis()
    {
        var html = _renderer.RenderContentHtml(MakeInvoice(), MakeCompany());

        html.Should().NotContain("inkl. Rabatt");
    }

    [Fact]
    public void RenderContentHtml_MitProzentualemRabatt_ZeigtProzentwert()
    {
        var html = _renderer.RenderContentHtml(
            MakeInvoice(discountType: DiscountType.Percentage, discountValue: 10), MakeCompany());

        html.Should().Contain("inkl. Rabatt: 10%");
    }

    [Fact]
    public void RenderContentHtml_MitAnzahlungen_ZeigtOffenenBetragStattGesamtbetrag()
    {
        var invoice = MakeInvoice(downPayments: [new DownPayment { Amount = 50 }]);

        var html = _renderer.RenderContentHtml(invoice, MakeCompany());

        // TotalGross = 119, davon 50 angezahlt -> AmountDue = 69
        html.Should().Contain("69,00");
        html.Should().NotContain("119,00");
    }

    [Fact]
    public void RenderContentHtml_OhneBic_ZeigtKeineBicZeile()
    {
        var html = _renderer.RenderContentHtml(MakeInvoice(), MakeCompany(bic: null));

        html.Should().NotContain("<strong>BIC:</strong>");
    }

    [Fact]
    public void RenderContentHtml_MitBic_ZeigtBicZeile()
    {
        var html = _renderer.RenderContentHtml(MakeInvoice(), MakeCompany(bic: "COBADEFFXXX"));

        html.Should().Contain("COBADEFFXXX");
    }

    // ─── RenderContentText ────────────────────────────────────────────────────

    [Fact]
    public void RenderContentText_EnthaeltRechnungsnummerUndBankverbindung()
    {
        var text = _renderer.RenderContentText(MakeInvoice(), MakeCompany());

        text.Should().Contain("R-2026-0001");
        text.Should().Contain("Sparkasse");
        text.Should().Contain("DE89370400440532013000");
    }

    // ─── ResolveGreeting ──────────────────────────────────────────────────────

    [Fact]
    public void ResolveGreeting_CustomMessageHatVorrang()
    {
        var invoice = MakeInvoice();
        invoice.Customer = new Customer { Salutation = "Sehr geehrte Frau Müller" };

        var greeting = _renderer.ResolveGreeting(invoice, "Individuelle Nachricht");

        greeting.Should().Be("Individuelle Nachricht");
    }

    [Fact]
    public void ResolveGreeting_OhneCustomMessage_NutztKundenAnrede()
    {
        var invoice = MakeInvoice();
        invoice.Customer = new Customer { Salutation = "Sehr geehrte Frau Müller" };

        var greeting = _renderer.ResolveGreeting(invoice, null);

        greeting.Should().Be("Sehr geehrte Frau Müller");
    }

    [Fact]
    public void ResolveGreeting_OhneCustomMessageUndOhneKundenAnrede_GibtNullZurueck()
    {
        var invoice = MakeInvoice();

        var greeting = _renderer.ResolveGreeting(invoice, null);

        greeting.Should().BeNull();
    }
}
