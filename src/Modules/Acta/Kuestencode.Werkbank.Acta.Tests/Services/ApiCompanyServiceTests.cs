using FluentAssertions;
using Kuestencode.Core.Enums;
using Kuestencode.Core.Models;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Werkbank.Acta.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Acta.Tests.Services;

public class ApiCompanyServiceTests
{
    private readonly Mock<IHostApiClient> _hostApiClient = new();

    private ApiCompanyService CreateService() => new(_hostApiClient.Object, Mock.Of<ILogger<ApiCompanyService>>());

    // ─── GetCompanyAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetCompanyAsync_MapptAlleFelderKorrekt()
    {
        _hostApiClient.Setup(c => c.GetCompanyAsync()).ReturnsAsync(new CompanyDto
        {
            Id = 7,
            OwnerFullName = "Max Mustermann",
            BusinessName = "Mustermann GmbH",
            IsKleinunternehmer = true,
            EmailLayout = "Betont",
            PdfLayout = "Strukturiert"
        });

        var company = await CreateService().GetCompanyAsync();

        company.Id.Should().Be(7);
        company.OwnerFullName.Should().Be("Max Mustermann");
        company.BusinessName.Should().Be("Mustermann GmbH");
        company.IsKleinunternehmer.Should().BeTrue();
        company.EmailLayout.Should().Be(EmailLayout.Betont);
        company.PdfLayout.Should().Be(PdfLayout.Strukturiert);
    }

    [Fact]
    public async Task GetCompanyAsync_UnbekannteLayoutWerte_FallenAufKlarZurueck()
    {
        _hostApiClient.Setup(c => c.GetCompanyAsync()).ReturnsAsync(new CompanyDto
        {
            EmailLayout = "NichtExistent",
            PdfLayout = "NichtExistent"
        });

        var company = await CreateService().GetCompanyAsync();

        company.EmailLayout.Should().Be(EmailLayout.Klar);
        company.PdfLayout.Should().Be(PdfLayout.Klar);
    }

    [Fact]
    public async Task GetCompanyAsync_KeineFirmaVorhanden_WirftInvalidOperationException()
    {
        _hostApiClient.Setup(c => c.GetCompanyAsync()).ReturnsAsync((CompanyDto?)null);

        var act = () => CreateService().GetCompanyAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetCompanyAsync_HostApiWirftException_WirdWeitergegeben()
    {
        _hostApiClient.Setup(c => c.GetCompanyAsync()).ThrowsAsync(new HttpRequestException("down"));

        var act = () => CreateService().GetCompanyAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ─── UpdateCompanyAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCompanyAsync_RuftHostApiAufUndLaedtAnschliessendNeu()
    {
        _hostApiClient.Setup(c => c.GetCompanyAsync()).ReturnsAsync(new CompanyDto { OwnerFullName = "Nach Update" });

        var result = await CreateService().UpdateCompanyAsync(new Company { OwnerFullName = "Vor Update" });

        _hostApiClient.Verify(c => c.UpdateCompanyAsync(It.IsAny<UpdateCompanyRequest>()), Times.Once);
        result.OwnerFullName.Should().Be("Nach Update");
    }

    // ─── HasCompanyDataAsync ────────────────────────────────────────────────

    [Fact]
    public async Task HasCompanyDataAsync_OwnerFullNameGesetzt_GibtTrueZurueck()
    {
        _hostApiClient.Setup(c => c.GetCompanyAsync()).ReturnsAsync(new CompanyDto { OwnerFullName = "Max Mustermann" });

        (await CreateService().HasCompanyDataAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task HasCompanyDataAsync_OwnerFullNameLeer_GibtFalseZurueck()
    {
        _hostApiClient.Setup(c => c.GetCompanyAsync()).ReturnsAsync(new CompanyDto { OwnerFullName = "" });

        (await CreateService().HasCompanyDataAsync()).Should().BeFalse();
    }

    // ─── IsEmailConfiguredAsync ─────────────────────────────────────────────

    [Fact]
    public async Task IsEmailConfiguredAsync_VollstaendigeSmtpDaten_GibtTrueZurueck()
    {
        _hostApiClient.Setup(c => c.GetCompanyAsync()).ReturnsAsync(new CompanyDto
        {
            SmtpHost = "smtp.example.com",
            SmtpPort = 587,
            SmtpUsername = "user"
        });

        (await CreateService().IsEmailConfiguredAsync()).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, 587, "user")]
    [InlineData("smtp.example.com", null, "user")]
    [InlineData("smtp.example.com", 587, null)]
    [InlineData("smtp.example.com", 0, "user")]
    public async Task IsEmailConfiguredAsync_UnvollstaendigeSmtpDaten_GibtFalseZurueck(string? host, int? port, string? username)
    {
        _hostApiClient.Setup(c => c.GetCompanyAsync()).ReturnsAsync(new CompanyDto
        {
            SmtpHost = host,
            SmtpPort = port,
            SmtpUsername = username
        });

        (await CreateService().IsEmailConfiguredAsync()).Should().BeFalse();
    }
}
