using System.Net;
using FluentAssertions;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests;

/// <summary>
/// Regressionstest für die im September 2026 gefundene Autorisierungslücke: Modul-Controller
/// müssen unauthentifizierte Requests ablehnen, unabhängig davon, ob sie direkt oder über den
/// Host-Reverse-Proxy erreicht werden.
/// </summary>
public class SettingsControllerAuthorizationTests : IClassFixture<SaldoWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SettingsControllerAuthorizationTests(SaldoWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSettings_OhneToken_GibtUnauthorizedZurueck()
    {
        var response = await _client.GetAsync("/api/saldo/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
