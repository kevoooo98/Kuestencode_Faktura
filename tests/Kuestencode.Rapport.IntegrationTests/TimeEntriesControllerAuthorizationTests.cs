using System.Net;
using FluentAssertions;
using Xunit;

namespace Kuestencode.Rapport.IntegrationTests;

/// <summary>
/// Regressionstest für die im September 2026 gefundene Autorisierungslücke: Modul-Controller
/// müssen unauthentifizierte Requests ablehnen, unabhängig davon, ob sie direkt oder über den
/// Host-Reverse-Proxy erreicht werden.
/// </summary>
public class TimeEntriesControllerAuthorizationTests : IClassFixture<RapportWebApplicationFactory>
{
    private readonly HttpClient _client;

    public TimeEntriesControllerAuthorizationTests(RapportWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetEntries_OhneToken_GibtUnauthorizedZurueck()
    {
        var response = await _client.GetAsync("/api/rapport/entries");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
