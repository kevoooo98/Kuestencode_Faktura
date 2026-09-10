using System.Net;
using FluentAssertions;
using Xunit;

namespace Kuestencode.Werkbank.Acta.Tests;

/// <summary>
/// Regressionstest für die im September 2026 gefundene Autorisierungslücke: Modul-Controller
/// müssen unauthentifizierte Requests ablehnen, unabhängig davon, ob sie direkt oder über den
/// Host-Reverse-Proxy erreicht werden.
/// </summary>
public class ProjectsControllerAuthorizationTests : IClassFixture<ActaWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ProjectsControllerAuthorizationTests(ActaWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_OhneToken_GibtUnauthorizedZurueck()
    {
        var response = await _client.GetAsync("/api/acta/projects");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
