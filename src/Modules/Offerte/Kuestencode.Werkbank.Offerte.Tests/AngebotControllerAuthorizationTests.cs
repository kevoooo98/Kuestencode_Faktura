using System.Net;
using FluentAssertions;
using Xunit;

namespace Kuestencode.Werkbank.Offerte.Tests;

/// <summary>
/// Regressionstest für die im September 2026 gefundene Autorisierungslücke: Modul-Controller
/// müssen unauthentifizierte Requests ablehnen, unabhängig davon, ob sie direkt oder über den
/// Host-Reverse-Proxy erreicht werden.
/// </summary>
public class AngebotControllerAuthorizationTests : IClassFixture<OfferteWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AngebotControllerAuthorizationTests(OfferteWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetPdfForPrint_OhneToken_GibtUnauthorizedZurueck()
    {
        var response = await _client.GetAsync($"/api/Angebot/{Guid.NewGuid()}/pdf-print");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
