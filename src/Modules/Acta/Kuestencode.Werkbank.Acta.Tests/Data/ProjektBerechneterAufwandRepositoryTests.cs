using FluentAssertions;
using Kuestencode.Werkbank.Acta.Data;
using Kuestencode.Werkbank.Acta.Data.Repositories;
using Kuestencode.Werkbank.Acta.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kuestencode.Werkbank.Acta.Tests.Data;

public class ProjektBerechneterAufwandRepositoryTests
{
    private static ProjektBerechneterAufwand MakeAufwand(Guid projectId, string belegnummer, DateTime berechnedAt) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Belegnummer = belegnummer,
        Lieferant = "Baustoffhandel Nord",
        Netto = 100m,
        Brutto = 119m,
        BerechnedAt = berechnedAt
    };

    [Fact]
    public async Task GetByProjektIdAsync_GibtEintraegeSortiertNachBerechnedAtZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var projectId = Guid.NewGuid();
        await using (var context = factory.CreateDbContext())
        {
            context.ProjektBerechneteAufwaende.AddRange(
                MakeAufwand(projectId, "B-2", new DateTime(2026, 2, 1)),
                MakeAufwand(projectId, "B-1", new DateTime(2026, 1, 1)));
            await context.SaveChangesAsync();
        }

        var repository = new ProjektBerechneterAufwandRepository(factory);
        var result = await repository.GetByProjektIdAsync(projectId);

        result.Should().HaveCount(2);
        result[0].Belegnummer.Should().Be("B-1");
        result[1].Belegnummer.Should().Be("B-2");
    }

    [Fact]
    public async Task GetBelegnummernByProjektIdAsync_GibtEindeutigeBelegnummernZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var projectId = Guid.NewGuid();
        await using (var context = factory.CreateDbContext())
        {
            context.ProjektBerechneteAufwaende.AddRange(
                MakeAufwand(projectId, "B-1", DateTime.UtcNow),
                MakeAufwand(projectId, "B-2", DateTime.UtcNow),
                MakeAufwand(Guid.NewGuid(), "B-Anderes-Projekt", DateTime.UtcNow));
            await context.SaveChangesAsync();
        }

        var repository = new ProjektBerechneterAufwandRepository(factory);
        var result = await repository.GetBelegnummernByProjektIdAsync(projectId);

        result.Should().BeEquivalentTo(["B-1", "B-2"]);
    }

    [Fact]
    public async Task AddRangeAsync_FuegtMehrereEintraegeHinzu()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var projectId = Guid.NewGuid();
        var repository = new ProjektBerechneterAufwandRepository(factory);

        await repository.AddRangeAsync(
        [
            MakeAufwand(projectId, "B-1", DateTime.UtcNow),
            MakeAufwand(projectId, "B-2", DateTime.UtcNow)
        ]);

        (await repository.GetByProjektIdAsync(projectId)).Should().HaveCount(2);
    }
}
