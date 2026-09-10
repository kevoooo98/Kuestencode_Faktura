using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Kuestencode.Faktura.Tests;

/// <summary>
/// Regressionstest für die im September 2026 gefundene Autorisierungslücke: Modul-Controller
/// müssen unauthentifizierte Requests ablehnen, unabhängig davon, ob sie direkt oder über den
/// Host-Reverse-Proxy erreicht werden. Ohne diesen Test käme die Lücke beim nächsten
/// Middleware-Umbau unbemerkt zurück, da alle anderen Tests bewusst einen gültigen Test-JWT
/// mitschicken (siehe InvoiceControllerTests.CreateTestAdminJwt).
/// </summary>
public class InvoiceControllerAuthorizationTests : IClassFixture<FakturaWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InvoiceControllerAuthorizationTests(FakturaWebApplicationFactory factory)
    {
        // Bewusst OHNE Authorization-Header — im Gegensatz zu InvoiceControllerTests.
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_OhneToken_GibtUnauthorizedZurueck()
    {
        var response = await _client.GetAsync("/api/Invoice");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_OhneToken_GibtUnauthorizedZurueck()
    {
        var response = await _client.PostAsJsonAsync("/api/Invoice", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
