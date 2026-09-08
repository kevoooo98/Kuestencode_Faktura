using FluentAssertions;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Kuestencode.Werkbank.Saldo.Domain.Enums;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Data;

public class ExportLogRepositoryTests
{
    private static ExportLog MakeLog(DateTime exportedAt) => new()
    {
        ExportTyp = ExportTyp.DatevBuchungsstapel,
        ZeitraumVon = new DateOnly(2026, 1, 1),
        ZeitraumBis = new DateOnly(2026, 1, 31),
        AnzahlBuchungen = 10,
        DateiName = "export.csv",
        DateiGroesse = 1024,
        ExportedAt = exportedAt,
        ExportedByUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task GetAllAsync_SortiertAbsteigendNachExportedAt()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.ExportLogs.AddRange(
                MakeLog(new DateTime(2026, 1, 1)),
                MakeLog(new DateTime(2026, 3, 1)));
            await context.SaveChangesAsync();
        }

        var repository = new ExportLogRepository(factory);
        var result = await repository.GetAllAsync();

        result.Should().HaveCount(2);
        result[0].ExportedAt.Should().Be(new DateTime(2026, 3, 1));
    }

    [Fact]
    public async Task AddAsync_VergibtNeueIdUndSpeichertLog()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var repository = new ExportLogRepository(factory);
        var log = MakeLog(DateTime.UtcNow);
        log.Id = Guid.Empty;

        var added = await repository.AddAsync(log);

        added.Id.Should().NotBe(Guid.Empty);
        (await repository.GetAllAsync()).Should().ContainSingle();
    }
}
