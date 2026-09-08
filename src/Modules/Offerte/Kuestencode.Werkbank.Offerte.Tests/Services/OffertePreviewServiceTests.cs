using FluentAssertions;
using Kuestencode.Core.Models;
using Kuestencode.Werkbank.Offerte.Services;
using Xunit;

namespace Kuestencode.Werkbank.Offerte.Tests.Services;

public class OffertePreviewServiceTests
{
    private readonly OffertePreviewService _service = new();

    [Fact]
    public void GenerateSampleAngebot_KeinKleinunternehmer_SetztSteuersatzAufNeunzehnProzent()
    {
        var angebot = _service.GenerateSampleAngebot(new Company { IsKleinunternehmer = false });

        angebot.Positionen.Should().HaveCount(3);
        angebot.Positionen.Should().OnlyContain(p => p.Steuersatz == 19);
    }

    [Fact]
    public void GenerateSampleAngebot_Kleinunternehmer_SetztSteuersatzAufNull()
    {
        var angebot = _service.GenerateSampleAngebot(new Company { IsKleinunternehmer = true });

        angebot.Positionen.Should().OnlyContain(p => p.Steuersatz == 0);
    }

    [Fact]
    public void GenerateSampleAngebot_ErzeugtGueltigesBeispielAngebot()
    {
        var angebot = _service.GenerateSampleAngebot(new Company { IsKleinunternehmer = false });

        angebot.Angebotsnummer.Should().NotBeNullOrWhiteSpace();
        angebot.Positionen.Should().NotBeEmpty();
        angebot.GueltigBis.Should().BeAfter(angebot.Erstelldatum);
    }
}
