using FluentAssertions;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.Contracts.Rapport;
using Kuestencode.Shared.Contracts.Recepta;
using Kuestencode.Werkbank.Acta.Data.Repositories;
using Kuestencode.Werkbank.Acta.Domain.Dtos;
using Kuestencode.Werkbank.Acta.Domain.Entities;
using Kuestencode.Werkbank.Acta.Services;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Acta.Tests.Services;

public class StundensatzServiceTests
{
    private readonly Mock<IProjektStundensatzRepository> _repo = new();
    private readonly Mock<IProjectRepository> _projectRepo = new();
    private readonly Mock<IProjektBerechneterAufwandRepository> _aufwandRepo = new();
    private readonly Mock<IRapportApiClient> _rapportClient = new();
    private readonly Mock<IReceptaApiClient> _receptaClient = new();
    private readonly Mock<IHostApiClient> _hostClient = new();

    private StundensatzService CreateService() => new(
        _repo.Object, _projectRepo.Object, _aufwandRepo.Object,
        _rapportClient.Object, _receptaClient.Object, _hostClient.Object);

    private static Project MakeProject(Guid id, int? externalId = null) => new()
    {
        Id = id,
        ProjectNumber = "P-0001",
        Name = "Testprojekt",
        CustomerId = 1,
        ExternalId = externalId
    };

    // ─── GetStundensaetzeAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetStundensaetzeAsync_MapptGespeicherteSaetze()
    {
        var projektId = Guid.NewGuid();
        _repo.Setup(r => r.GetByProjektIdAsync(projektId)).ReturnsAsync(
        [
            new ProjektStundensatz { ProjectId = projektId, RolleId = 1, RolleName = "Meister", Stundensatz = 65m }
        ]);

        var result = await CreateService().GetStundensaetzeAsync(projektId);

        result.Should().ContainSingle();
        result[0].RolleName.Should().Be("Meister");
        result[0].Stundensatz.Should().Be(65m);
    }

    // ─── UpsertStundensatzAsync ─────────────────────────────────────────────

    [Fact]
    public async Task UpsertStundensatzAsync_KeinVorhandenerSatz_FuegtNeuenHinzu()
    {
        var projektId = Guid.NewGuid();
        _repo.Setup(r => r.GetByProjektIdAndRolleAsync(projektId, 1)).ReturnsAsync((ProjektStundensatz?)null);

        await CreateService().UpsertStundensatzAsync(projektId, 1, "Geselle", 45m);

        _repo.Verify(r => r.AddAsync(It.Is<ProjektStundensatz>(s =>
            s.ProjectId == projektId && s.RolleId == 1 && s.RolleName == "Geselle" && s.Stundensatz == 45m)), Times.Once);
    }

    [Fact]
    public async Task UpsertStundensatzAsync_VorhandenerSatz_AktualisiertIhn()
    {
        var projektId = Guid.NewGuid();
        var existing = new ProjektStundensatz { ProjectId = projektId, RolleId = 1, RolleName = "Alt", Stundensatz = 40m };
        _repo.Setup(r => r.GetByProjektIdAndRolleAsync(projektId, 1)).ReturnsAsync(existing);

        await CreateService().UpsertStundensatzAsync(projektId, 1, "Neu", 50m);

        existing.RolleName.Should().Be("Neu");
        existing.Stundensatz.Should().Be(50m);
        _repo.Verify(r => r.UpdateAsync(existing), Times.Once);
        _repo.Verify(r => r.AddAsync(It.IsAny<ProjektStundensatz>()), Times.Never);
    }

    // ─── GetProjektAbrechnungAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetProjektAbrechnungAsync_OhneExternalId_KeineRapportAufrufeAberReceptaMitInternerId()
    {
        var projektId = Guid.NewGuid();
        _repo.Setup(r => r.GetByProjektIdAsync(projektId)).ReturnsAsync([]);
        _projectRepo.Setup(p => p.GetByIdAsync(projektId)).ReturnsAsync(MakeProject(projektId));
        _aufwandRepo.Setup(a => a.GetByProjektIdAsync(projektId)).ReturnsAsync([]);
        _receptaClient.Setup(r => r.GetProjectExpensesAsync(It.IsAny<Guid>())).ReturnsAsync((Kuestencode.Shared.Contracts.Recepta.ProjectExpensesResponseDto?)null);

        var abrechnung = await CreateService().GetProjektAbrechnungAsync(projektId);

        abrechnung.Positionen.Should().BeEmpty();
        abrechnung.MaterialNetto.Should().Be(0);
        _rapportClient.Verify(r => r.GetProjectHoursByTypeAsync(It.IsAny<int>()), Times.Never);
        _receptaClient.Verify(r => r.GetProjectExpensesAsync(projektId), Times.Once);
    }

    [Fact]
    public async Task GetProjektAbrechnungAsync_MitExternalId_BerechnetPositionenUndMaterial()
    {
        var projektId = Guid.NewGuid();
        var externalId = 42;
        _repo.Setup(r => r.GetByProjektIdAsync(projektId)).ReturnsAsync(
        [
            new ProjektStundensatz { ProjectId = projektId, RolleId = 1, RolleName = "Meister", Stundensatz = 60m }
        ]);
        _projectRepo.Setup(p => p.GetByIdAsync(projektId)).ReturnsAsync(MakeProject(projektId, externalId));
        _aufwandRepo.Setup(a => a.GetByProjektIdAsync(projektId)).ReturnsAsync([]);

        _rapportClient.Setup(r => r.GetProjectHoursByTypeAsync(externalId)).ReturnsAsync(new ProjectHoursByTypeResponseDto
        {
            ProjectId = externalId,
            StundenByRolle = [new ProjectHoursByRolleDto { RolleId = 1, RolleName = "Meister", Stunden = 10 }],
            InvoicedStundenByRolle = [new ProjectHoursByRolleDto { RolleId = 1, RolleName = "Meister", Stunden = 4 }]
        });

        var teamMemberId = Guid.NewGuid();
        _rapportClient.Setup(r => r.GetProjectHoursByMemberAsync(externalId)).ReturnsAsync(new ProjectHoursByMemberResponseDto
        {
            ProjectId = externalId,
            StundenByMitarbeiter = [new ProjectHoursByMemberDto { TeamMemberId = teamMemberId, TeamMemberName = "Max", Stunden = 8 }]
        });
        _hostClient.Setup(h => h.GetTeamMembersAsync()).ReturnsAsync(
        [
            new TeamMemberDto { Id = teamMemberId, DisplayName = "Max", Kostensatz = 35m }
        ]);

        _receptaClient.Setup(r => r.GetProjectExpensesAsync(It.IsAny<Guid>())).ReturnsAsync(new ProjectExpensesResponseDto
        {
            TotalNet = 500m,
            TotalGross = 595m
        });

        var abrechnung = await CreateService().GetProjektAbrechnungAsync(projektId);

        abrechnung.Positionen.Should().ContainSingle(p => p.RolleId == 1 && p.Stunden == 10 && p.Stundensatz == 60m);
        abrechnung.BerechnetePositionen.Should().ContainSingle(p => p.RolleId == 1 && p.Stunden == 4 && p.Stundensatz == 60m);
        abrechnung.Arbeitskosten.Should().ContainSingle(a => a.MitarbeiterId == teamMemberId && a.Kostensatz == 35m);
        abrechnung.MaterialNetto.Should().Be(500m);
        abrechnung.MaterialBrutto.Should().Be(595m);
    }

    [Fact]
    public async Task GetProjektAbrechnungAsync_RapportNichtErreichbar_PositionenBleibenAusStundensaetzen()
    {
        var projektId = Guid.NewGuid();
        var externalId = 42;
        _repo.Setup(r => r.GetByProjektIdAsync(projektId)).ReturnsAsync(
        [
            new ProjektStundensatz { ProjectId = projektId, RolleId = 1, RolleName = "Meister", Stundensatz = 60m }
        ]);
        _projectRepo.Setup(p => p.GetByIdAsync(projektId)).ReturnsAsync(MakeProject(projektId, externalId));
        _aufwandRepo.Setup(a => a.GetByProjektIdAsync(projektId)).ReturnsAsync([]);
        _rapportClient.Setup(r => r.GetProjectHoursByTypeAsync(externalId)).ThrowsAsync(new HttpRequestException("down"));
        _rapportClient.Setup(r => r.GetProjectHoursByMemberAsync(externalId)).ThrowsAsync(new HttpRequestException("down"));
        _receptaClient.Setup(r => r.GetProjectExpensesAsync(It.IsAny<Guid>())).ThrowsAsync(new HttpRequestException("down"));

        var abrechnung = await CreateService().GetProjektAbrechnungAsync(projektId);

        abrechnung.Positionen.Should().ContainSingle(p => p.RolleId == 1 && p.Stunden == 0 && p.Stundensatz == 60m);
        abrechnung.Arbeitskosten.Should().BeEmpty();
        abrechnung.MaterialNetto.Should().Be(0);
    }

    [Fact]
    public async Task GetProjektAbrechnungAsync_BerechneteAufwaendeVorhanden_VerwendetDerenSummeStattProjectFallback()
    {
        var projektId = Guid.NewGuid();
        _repo.Setup(r => r.GetByProjektIdAsync(projektId)).ReturnsAsync([]);
        var project = MakeProject(projektId);
        project.MaterialBerechnedNetto = 1000m;
        project.MaterialBerechnedBrutto = 1190m;
        _projectRepo.Setup(p => p.GetByIdAsync(projektId)).ReturnsAsync(project);
        _aufwandRepo.Setup(a => a.GetByProjektIdAsync(projektId)).ReturnsAsync(
        [
            new ProjektBerechneterAufwand { ProjectId = projektId, Belegnummer = "B-1", Lieferant = "Lieferant", Netto = 100m, Brutto = 119m }
        ]);

        var abrechnung = await CreateService().GetProjektAbrechnungAsync(projektId);

        abrechnung.MaterialBerechnedNetto.Should().Be(100m);
        abrechnung.MaterialBerechnedBrutto.Should().Be(119m);
        abrechnung.BerechneteAufwaende.Should().ContainSingle(a => a.Belegnummer == "B-1");
    }

    // ─── MarkProjectTimeEntriesAsInvoicedAsync ──────────────────────────────

    [Fact]
    public async Task MarkProjectTimeEntriesAsInvoicedAsync_DelegiertAnRapportClient()
    {
        await CreateService().MarkProjectTimeEntriesAsInvoicedAsync(42);

        _rapportClient.Verify(r => r.MarkProjectTimeEntriesAsInvoicedAsync(42), Times.Once);
    }

    // ─── AddBerechneteAufwaendeAsync ────────────────────────────────────────

    [Fact]
    public async Task AddBerechneteAufwaendeAsync_FiltertBereitsVorhandeneBelegnummern()
    {
        var projektId = Guid.NewGuid();
        _aufwandRepo.Setup(a => a.GetBelegnummernByProjektIdAsync(projektId)).ReturnsAsync(new HashSet<string> { "B-1" });
        _projectRepo.Setup(p => p.GetByIdAsync(projektId)).ReturnsAsync(MakeProject(projektId));

        await CreateService().AddBerechneteAufwaendeAsync(projektId,
        [
            new BerechneterAufwandDto { Belegnummer = "B-1", Netto = 50m, Brutto = 59m },
            new BerechneterAufwandDto { Belegnummer = "B-2", Netto = 100m, Brutto = 119m }
        ]);

        _aufwandRepo.Verify(a => a.AddRangeAsync(It.Is<IEnumerable<ProjektBerechneterAufwand>>(
            list => list.Count() == 1 && list.Single().Belegnummer == "B-2")), Times.Once);
    }

    [Fact]
    public async Task AddBerechneteAufwaendeAsync_AktualisiertProjektSummen()
    {
        var projektId = Guid.NewGuid();
        var project = MakeProject(projektId);
        project.MaterialBerechnedNetto = 10m;
        project.MaterialBerechnedBrutto = 11.9m;
        _aufwandRepo.Setup(a => a.GetBelegnummernByProjektIdAsync(projektId)).ReturnsAsync(new HashSet<string>());
        _projectRepo.Setup(p => p.GetByIdAsync(projektId)).ReturnsAsync(project);

        await CreateService().AddBerechneteAufwaendeAsync(projektId,
        [
            new BerechneterAufwandDto { Belegnummer = "B-1", Netto = 100m, Brutto = 119m }
        ]);

        project.MaterialBerechnedNetto.Should().Be(110m);
        project.MaterialBerechnedBrutto.Should().Be(130.9m);
        _projectRepo.Verify(p => p.UpdateAsync(project), Times.Once);
    }

    [Fact]
    public async Task AddBerechneteAufwaendeAsync_KeineNeuenBelege_BrichtOhneSpeichernAb()
    {
        var projektId = Guid.NewGuid();
        _aufwandRepo.Setup(a => a.GetBelegnummernByProjektIdAsync(projektId)).ReturnsAsync(new HashSet<string> { "B-1" });

        await CreateService().AddBerechneteAufwaendeAsync(projektId,
        [
            new BerechneterAufwandDto { Belegnummer = "B-1", Netto = 50m, Brutto = 59m }
        ]);

        _aufwandRepo.Verify(a => a.AddRangeAsync(It.IsAny<IEnumerable<ProjektBerechneterAufwand>>()), Times.Never);
        _projectRepo.Verify(p => p.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
    }
}
