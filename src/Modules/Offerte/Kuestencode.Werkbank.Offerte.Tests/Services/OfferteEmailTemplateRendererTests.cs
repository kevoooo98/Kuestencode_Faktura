using FluentAssertions;
using Kuestencode.Core.Models;
using Kuestencode.Werkbank.Offerte.Domain.Entities;
using Kuestencode.Werkbank.Offerte.Services.Email;
using Xunit;

namespace Kuestencode.Werkbank.Offerte.Tests.Services;

public class OfferteEmailTemplateRendererTests
{
    private readonly OfferteEmailTemplateRenderer _renderer = new();

    private static Angebot MakeAngebot() => new()
    {
        Angebotsnummer = "AN-2026-001",
        Erstelldatum = new DateTime(2026, 1, 15),
        GueltigBis = new DateTime(2026, 2, 15),
        Positionen = new List<Angebotsposition>
        {
            new() { Menge = 2, Einzelpreis = 100m, Steuersatz = 19m }
        }
    };

    // ─── RenderContentHtml ──────────────────────────────────────────────────

    [Fact]
    public void RenderContentHtml_EnthaeltAngebotsnummerUndBetrag()
    {
        var html = _renderer.RenderContentHtml(MakeAngebot());

        html.Should().Contain("AN-2026-001");
        html.Should().Contain("15.01.2026");
        html.Should().Contain("15.02.2026");
    }

    // ─── RenderContentText ──────────────────────────────────────────────────

    [Fact]
    public void RenderContentText_EnthaeltAngebotsnummerUndDaten()
    {
        var text = _renderer.RenderContentText(MakeAngebot());

        text.Should().Contain("AN-2026-001");
        text.Should().Contain("ANGEBOTSDETAILS");
        text.Should().Contain("15.01.2026");
    }

    // ─── ResolveGreeting ────────────────────────────────────────────────────

    [Fact]
    public void ResolveGreeting_CustomMessageGesetzt_GibtCustomMessageZurueck()
    {
        var kunde = new Customer { Salutation = "Sehr geehrte Frau Muster," };

        var result = _renderer.ResolveGreeting(kunde, "Individuelle Nachricht");

        result.Should().Be("Individuelle Nachricht");
    }

    [Fact]
    public void ResolveGreeting_KeineCustomMessage_VerwendetKundenSalutation()
    {
        var kunde = new Customer { Salutation = "Sehr geehrte Frau Muster," };

        var result = _renderer.ResolveGreeting(kunde, null);

        result.Should().Be("Sehr geehrte Frau Muster,");
    }

    [Fact]
    public void ResolveGreeting_WederCustomMessageNochSalutation_GibtNullZurueck()
    {
        var kunde = new Customer { Salutation = null };

        var result = _renderer.ResolveGreeting(kunde, "  ");

        result.Should().BeNull();
    }
}
