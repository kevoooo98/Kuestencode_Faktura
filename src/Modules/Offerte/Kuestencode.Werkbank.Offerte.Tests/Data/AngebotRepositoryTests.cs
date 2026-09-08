using FluentAssertions;
using Kuestencode.Werkbank.Offerte.Data;
using Kuestencode.Werkbank.Offerte.Data.Repositories;
using Kuestencode.Werkbank.Offerte.Domain.Entities;
using Kuestencode.Werkbank.Offerte.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kuestencode.Werkbank.Offerte.Tests.Data;

public class AngebotRepositoryTests : IDisposable
{
    private readonly DbContextOptions<OfferteDbContext> _options;
    private readonly OfferteDbContext _dbContext;
    private readonly AngebotRepository _repository;

    public AngebotRepositoryTests()
    {
        _options = new DbContextOptionsBuilder<OfferteDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new OfferteDbContext(_options);
        _repository = new AngebotRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Neuer Context nötig, damit das Include(...).OrderBy(...) nicht durch bereits
    // im ChangeTracker vorhandene (unsortierte) Positionen überschrieben wird.
    private AngebotRepository CreateRepositoryOnFreshContext() => new(new OfferteDbContext(_options));

    private static Angebot MakeAngebot(
        string nummer = "AN-0001",
        int kundeId = 1,
        AngebotStatus status = AngebotStatus.Entwurf,
        DateTime? erstelldatum = null,
        DateTime? gueltigBis = null) => new()
    {
        Id = Guid.NewGuid(),
        Angebotsnummer = nummer,
        KundeId = kundeId,
        Status = status,
        Erstelldatum = erstelldatum ?? DateTime.UtcNow,
        GueltigBis = gueltigBis ?? DateTime.UtcNow.AddDays(14)
    };

    // ─── GetByIdAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_VorhandenesAngebot_LaedtMitPositionenSortiert()
    {
        var angebot = MakeAngebot();
        angebot.Positionen.Add(new Angebotsposition { Id = Guid.NewGuid(), AngebotId = angebot.Id, Position = 2, Text = "Zweite" });
        angebot.Positionen.Add(new Angebotsposition { Id = Guid.NewGuid(), AngebotId = angebot.Id, Position = 1, Text = "Erste" });
        await _repository.AddAsync(angebot);

        var result = await CreateRepositoryOnFreshContext().GetByIdAsync(angebot.Id);

        result.Should().NotBeNull();
        result!.Positionen.Should().HaveCount(2);
        result.Positionen[0].Text.Should().Be("Erste");
    }

    [Fact]
    public async Task GetByIdAsync_UnbekannteId_GibtNullZurueck()
    {
        (await _repository.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    // ─── GetByNummerAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetByNummerAsync_FindetAngebotAnhandNummer()
    {
        await _repository.AddAsync(MakeAngebot("AN-9999"));

        var result = await _repository.GetByNummerAsync("AN-9999");

        result.Should().NotBeNull();
        result!.Angebotsnummer.Should().Be("AN-9999");
    }

    // ─── GetAllAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_SortiertAbsteigendNachErstelldatum()
    {
        await _repository.AddAsync(MakeAngebot("AN-1", erstelldatum: new DateTime(2026, 1, 1)));
        await _repository.AddAsync(MakeAngebot("AN-2", erstelldatum: new DateTime(2026, 3, 1)));

        var result = await _repository.GetAllAsync();

        result.Should().HaveCount(2);
        result[0].Angebotsnummer.Should().Be("AN-2");
    }

    // ─── GetByKundeAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByKundeAsync_GibtNurAngeboteDesKundenZurueck()
    {
        await _repository.AddAsync(MakeAngebot("AN-1", kundeId: 1));
        await _repository.AddAsync(MakeAngebot("AN-2", kundeId: 2));

        var result = await _repository.GetByKundeAsync(1);

        result.Should().ContainSingle(a => a.Angebotsnummer == "AN-1");
    }

    // ─── GetByStatusAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetByStatusAsync_GibtNurAngeboteMitPassendemStatusZurueck()
    {
        await _repository.AddAsync(MakeAngebot("AN-1", status: AngebotStatus.Entwurf));
        await _repository.AddAsync(MakeAngebot("AN-2", status: AngebotStatus.Versendet));

        var result = await _repository.GetByStatusAsync(AngebotStatus.Versendet);

        result.Should().ContainSingle(a => a.Angebotsnummer == "AN-2");
    }

    // ─── GetAbgelaufeneAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetAbgelaufeneAsync_NurVersendeteUndAbgelaufeneWerdenGefunden()
    {
        await _repository.AddAsync(MakeAngebot("AN-Abgelaufen", status: AngebotStatus.Versendet, gueltigBis: DateTime.UtcNow.AddDays(-5)));
        await _repository.AddAsync(MakeAngebot("AN-Gueltig", status: AngebotStatus.Versendet, gueltigBis: DateTime.UtcNow.AddDays(5)));
        await _repository.AddAsync(MakeAngebot("AN-Entwurf", status: AngebotStatus.Entwurf, gueltigBis: DateTime.UtcNow.AddDays(-5)));

        var result = await _repository.GetAbgelaufeneAsync();

        result.Should().ContainSingle(a => a.Angebotsnummer == "AN-Abgelaufen");
    }

    // ─── AddAsync / UpdateAsync ─────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_SpeichertNeuesAngebot()
    {
        var angebot = MakeAngebot();

        await _repository.AddAsync(angebot);

        (await _repository.GetByIdAsync(angebot.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_AktualisiertBestehendesAngebot()
    {
        var angebot = MakeAngebot();
        await _repository.AddAsync(angebot);

        angebot.Referenz = "Neue Referenz";
        await _repository.UpdateAsync(angebot);

        var updated = await _repository.GetByIdAsync(angebot.Id);
        updated!.Referenz.Should().Be("Neue Referenz");
    }

    // ─── DeleteAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_AngebotImEntwurf_WirdEntfernt()
    {
        var angebot = MakeAngebot(status: AngebotStatus.Entwurf);
        await _repository.AddAsync(angebot);

        await _repository.DeleteAsync(angebot.Id);

        (await _repository.GetByIdAsync(angebot.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnbekannteId_WirftInvalidOperationException()
    {
        var act = () => _repository.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_AngebotNichtImEntwurf_WirftInvalidOperationException()
    {
        var angebot = MakeAngebot(status: AngebotStatus.Versendet);
        await _repository.AddAsync(angebot);

        var act = () => _repository.DeleteAsync(angebot.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _repository.GetByIdAsync(angebot.Id)).Should().NotBeNull();
    }

    // ─── ExistiertNummerAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ExistiertNummerAsync_PrueftVorhandensein()
    {
        await _repository.AddAsync(MakeAngebot("AN-EXIST"));

        (await _repository.ExistiertNummerAsync("AN-EXIST")).Should().BeTrue();
        (await _repository.ExistiertNummerAsync("AN-UNKNOWN")).Should().BeFalse();
    }
}
