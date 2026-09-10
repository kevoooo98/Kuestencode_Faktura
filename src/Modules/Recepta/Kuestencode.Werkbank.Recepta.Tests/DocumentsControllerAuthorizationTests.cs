using System.Net;
using FluentAssertions;
using Xunit;

namespace Kuestencode.Werkbank.Recepta.Tests;

/// <summary>
/// Regressionstest für die im September 2026 gefundene Autorisierungslücke: Modul-Controller
/// müssen unauthentifizierte Requests ablehnen, unabhängig davon, ob sie direkt oder über den
/// Host-Reverse-Proxy erreicht werden.
/// </summary>
public class DocumentsControllerAuthorizationTests : IClassFixture<ReceptaWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DocumentsControllerAuthorizationTests(ReceptaWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_OhneToken_GibtUnauthorizedZurueck()
    {
        var response = await _client.GetAsync("/api/recepta/documents");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
