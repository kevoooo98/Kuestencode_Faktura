using FluentAssertions;
using Kuestencode.Werkbank.Acta.Data;
using Kuestencode.Werkbank.Acta.Data.Repositories;
using Kuestencode.Werkbank.Acta.Domain.Entities;
using Xunit;

namespace Kuestencode.Werkbank.Acta.Tests.Data;

public class ProjektStundensatzRepositoryTests
{
    private static ProjektStundensatz MakeStundensatz(Guid projectId, int rolleId, decimal stundensatz) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        RolleId = rolleId,
        RolleName = $"Rolle {rolleId}",
        Stundensatz = stundensatz
    };

    [Fact]
    public async Task GetByProjektIdAsync_GibtAlleStundensaetzeDesProjektsZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var projectId = Guid.NewGuid();
        await using (var context = factory.CreateDbContext())
        {
            context.ProjektStundensaetze.AddRange(
                MakeStundensatz(projectId, 1, 80m),
                MakeStundensatz(projectId, 2, 100m),
                MakeStundensatz(Guid.NewGuid(), 1, 90m));
            await context.SaveChangesAsync();
        }

        var repository = new ProjektStundensatzRepository(factory);
        var result = await repository.GetByProjektIdAsync(projectId);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByProjektIdAndRolleAsync_FindetPassendenEintrag()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var projectId = Guid.NewGuid();
        await using (var context = factory.CreateDbContext())
        {
            context.ProjektStundensaetze.Add(MakeStundensatz(projectId, 5, 120m));
            await context.SaveChangesAsync();
        }

        var repository = new ProjektStundensatzRepository(factory);
        var result = await repository.GetByProjektIdAndRolleAsync(projectId, 5);

        result.Should().NotBeNull();
        result!.Stundensatz.Should().Be(120m);
    }

    [Fact]
    public async Task GetByProjektIdAndRolleAsync_KeinPassenderEintrag_GibtNullZurueck()
    {
        var repository = new ProjektStundensatzRepository(TestDbContextFactory.CreateInMemory());

        var result = await repository.GetByProjektIdAndRolleAsync(Guid.NewGuid(), 99);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_SpeichertNeuenStundensatz()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var repository = new ProjektStundensatzRepository(factory);
        var projectId = Guid.NewGuid();

        await repository.AddAsync(MakeStundensatz(projectId, 3, 75m));

        (await repository.GetByProjektIdAsync(projectId)).Should().ContainSingle(s => s.Stundensatz == 75m);
    }

    [Fact]
    public async Task UpdateAsync_AktualisiertBestehendenStundensatz()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var projectId = Guid.NewGuid();
        var stundensatz = MakeStundensatz(projectId, 1, 80m);
        await using (var context = factory.CreateDbContext())
        {
            context.ProjektStundensaetze.Add(stundensatz);
            await context.SaveChangesAsync();
        }

        var repository = new ProjektStundensatzRepository(factory);
        stundensatz.Stundensatz = 95m;
        await repository.UpdateAsync(stundensatz);

        var updated = await repository.GetByProjektIdAndRolleAsync(projectId, 1);
        updated!.Stundensatz.Should().Be(95m);
    }
}
