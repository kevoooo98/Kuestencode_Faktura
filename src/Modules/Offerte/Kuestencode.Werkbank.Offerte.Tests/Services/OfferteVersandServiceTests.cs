using FluentAssertions;
using Kuestencode.Core.Interfaces;
using Kuestencode.Core.Models;
using Kuestencode.Werkbank.Offerte.Data.Repositories;
using Kuestencode.Werkbank.Offerte.Domain.Entities;
using Kuestencode.Werkbank.Offerte.Domain.Enums;
using Kuestencode.Werkbank.Offerte.Domain.Services;
using Kuestencode.Werkbank.Offerte.Services;
using Kuestencode.Werkbank.Offerte.Services.Email;
using Kuestencode.Werkbank.Offerte.Services.Pdf;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Offerte.Tests.Services;

public class OfferteVersandServiceTests
{
    private readonly Mock<IAngebotRepository> _repository = new();
    private readonly Mock<IOffertePdfService> _pdfService = new();
    private readonly Mock<IEmailEngine> _emailEngine = new();
    private readonly Mock<ICustomerService> _customerService = new();
    private readonly Mock<ICompanyService> _companyService = new();
    private readonly Mock<IOfferteSettingsService> _settingsService = new();
    private readonly Mock<IOfferteEmailTemplateRenderer> _templateRenderer = new();

    private OfferteVersandService CreateService() => new(
        _repository.Object,
        _pdfService.Object,
        _emailEngine.Object,
        _customerService.Object,
        _companyService.Object,
        _settingsService.Object,
        _templateRenderer.Object,
        new AngebotStatusService(),
        Mock.Of<ILogger<OfferteVersandService>>());

    private static Angebot MakeAngebot(AngebotStatus status = AngebotStatus.Entwurf) => new()
    {
        Id = Guid.NewGuid(),
        Angebotsnummer = "AN-0001",
        KundeId = 42,
        Status = status,
        Erstelldatum = DateTime.UtcNow,
        GueltigBis = DateTime.UtcNow.AddDays(14)
    };

    private void SetupHappyPath(Angebot angebot, Customer? kunde = null)
    {
        _repository.Setup(r => r.GetByIdAsync(angebot.Id)).ReturnsAsync(angebot);
        _customerService.Setup(c => c.GetByIdAsync(angebot.KundeId))
            .ReturnsAsync(kunde ?? new Customer { Id = angebot.KundeId, Email = "kunde@example.com" });
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(new Company { BusinessName = "Meine Firma" });
        _settingsService.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new OfferteSettings());
        _pdfService.Setup(p => p.Erstelle(It.IsAny<Angebot>(), It.IsAny<Customer>(), It.IsAny<Company>(), It.IsAny<OfferteSettings>()))
            .Returns([1, 2, 3]);
        _templateRenderer.Setup(t => t.RenderContentHtml(angebot)).Returns("<p>html</p>");
        _templateRenderer.Setup(t => t.RenderContentText(angebot)).Returns("text");
        _templateRenderer.Setup(t => t.ResolveGreeting(It.IsAny<Customer>(), It.IsAny<string?>())).Returns((string?)null);
        _emailEngine.Setup(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(true);
    }

    // ─── VersendeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task VersendeAsync_UnbekanntesAngebot_WirftInvalidOperationException()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Angebot?)null);

        var act = () => CreateService().VersendeAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task VersendeAsync_AngebotNichtImEntwurf_WirftInvalidOperationException()
    {
        var angebot = MakeAngebot(AngebotStatus.Versendet);
        _repository.Setup(r => r.GetByIdAsync(angebot.Id)).ReturnsAsync(angebot);

        var act = () => CreateService().VersendeAsync(angebot.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task VersendeAsync_UnbekannterKunde_WirftInvalidOperationException()
    {
        var angebot = MakeAngebot();
        _repository.Setup(r => r.GetByIdAsync(angebot.Id)).ReturnsAsync(angebot);
        _customerService.Setup(c => c.GetByIdAsync(angebot.KundeId)).ReturnsAsync((Customer?)null);

        var act = () => CreateService().VersendeAsync(angebot.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task VersendeAsync_KundeOhneEmailUndKeinExpliziterEmpfaenger_WirftInvalidOperationException()
    {
        var angebot = MakeAngebot();
        _repository.Setup(r => r.GetByIdAsync(angebot.Id)).ReturnsAsync(angebot);
        _customerService.Setup(c => c.GetByIdAsync(angebot.KundeId)).ReturnsAsync(new Customer { Id = angebot.KundeId, Email = null });
        _companyService.Setup(c => c.GetCompanyAsync()).ReturnsAsync(new Company());
        _settingsService.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new OfferteSettings());

        var act = () => CreateService().VersendeAsync(angebot.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task VersendeAsync_ErfolgreicherVersand_AktualisiertStatusUndTracking()
    {
        var angebot = MakeAngebot();
        SetupHappyPath(angebot);

        var result = await CreateService().VersendeAsync(angebot.Id);

        result.Should().BeTrue();
        angebot.Status.Should().Be(AngebotStatus.Versendet);
        angebot.EmailGesendetAn.Should().Be("kunde@example.com");
        angebot.EmailAnzahl.Should().Be(1);
        _repository.Verify(r => r.UpdateAsync(angebot), Times.Once);
    }

    [Fact]
    public async Task VersendeAsync_ExpliziterEmpfaenger_UeberschreibtKundenEmail()
    {
        var angebot = MakeAngebot();
        SetupHappyPath(angebot);

        await CreateService().VersendeAsync(angebot.Id, empfaengerEmail: "andere@example.com");

        angebot.EmailGesendetAn.Should().Be("andere@example.com");
        _emailEngine.Verify(e => e.SendEmailAsync(
            "andere@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<IEnumerable<EmailAttachment>?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task VersendeAsync_EmailEngineWirftException_StatusWirdNichtAktualisiertUndExceptionPropagiert()
    {
        var angebot = MakeAngebot();
        SetupHappyPath(angebot);
        _emailEngine.Setup(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<EmailAttachment>?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        var act = () => CreateService().VersendeAsync(angebot.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        angebot.Status.Should().Be(AngebotStatus.Entwurf);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Angebot>()), Times.Never);
    }
}
