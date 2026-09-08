using FluentAssertions;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Werkbank.Offerte.Data;
using Kuestencode.Werkbank.Offerte.Data.Services;
using Kuestencode.Werkbank.Offerte.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Offerte.Tests.Data;

public class AngebotsnummernServiceTests : IDisposable
{
    private readonly OfferteDbContext _dbContext;
    private readonly Mock<IHostApiClient> _hostApiClient = new();

    public AngebotsnummernServiceTests()
    {
        var options = new DbContextOptionsBuilder<OfferteDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new OfferteDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    private AngebotsnummernService CreateService() => new(_dbContext, _hostApiClient.Object);

    // ─── NaechsteNummerAsync ────────────────────────────────────────────────

    [Fact]
    public async Task NaechsteNummerAsync_KeineEinstellungen_VerwendetStandardformat()
    {
        _hostApiClient.Setup(c => c.GetNumberFormatSettingsAsync()).ReturnsAsync((NumberFormatSettingsDto?)null);

        var nummer = await CreateService().NaechsteNummerAsync();

        nummer.Should().MatchRegex(@"^ANG-\d{4}-\d{5}$");
        nummer.Should().EndWith("00001");
    }

    [Fact]
    public async Task NaechsteNummerAsync_LeeresFormat_VerwendetStandardformat()
    {
        _hostApiClient.Setup(c => c.GetNumberFormatSettingsAsync())
            .ReturnsAsync(new NumberFormatSettingsDto { QuoteFormat = "   " });

        var nummer = await CreateService().NaechsteNummerAsync();

        nummer.Should().StartWith("ANG-").And.EndWith("00001");
    }

    [Fact]
    public async Task NaechsteNummerAsync_KonfiguriertesFormat_WirdVerwendet()
    {
        _hostApiClient.Setup(c => c.GetNumberFormatSettingsAsync())
            .ReturnsAsync(new NumberFormatSettingsDto { QuoteFormat = "Q-XXX" });

        var nummer = await CreateService().NaechsteNummerAsync();

        nummer.Should().Be("Q-001");
    }

    [Fact]
    public async Task NaechsteNummerAsync_BerechnetAufBasisVorhandenerNummern()
    {
        _hostApiClient.Setup(c => c.GetNumberFormatSettingsAsync())
            .ReturnsAsync(new NumberFormatSettingsDto { QuoteFormat = "Q-XXX" });
        _dbContext.Angebote.Add(new Angebot { Id = Guid.NewGuid(), Angebotsnummer = "Q-003", KundeId = 1, Erstelldatum = DateTime.UtcNow, GueltigBis = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        var nummer = await CreateService().NaechsteNummerAsync();

        nummer.Should().Be("Q-004");
    }

    // ─── ExistiertAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExistiertAsync_PrueftVorhandensein()
    {
        _dbContext.Angebote.Add(new Angebot { Id = Guid.NewGuid(), Angebotsnummer = "Q-EXIST", KundeId = 1, Erstelldatum = DateTime.UtcNow, GueltigBis = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        (await service.ExistiertAsync("Q-EXIST")).Should().BeTrue();
        (await service.ExistiertAsync("Q-UNKNOWN")).Should().BeFalse();
    }
}
