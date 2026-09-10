using FluentAssertions;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Data;

public class PeriodCloseRepositoryTests
{
    private static readonly DateOnly Von = new(2026, 1, 1);
    private static readonly DateOnly Bis = new(2026, 3, 31);

    private static PeriodClose MakeClose(DateOnly von, DateOnly bis, DateTime closedAt) => new()
    {
        ZeitraumVon = von,
        ZeitraumBis = bis,
        ClosedAt = closedAt,
        ClosedByUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task AddAsync_VergibtNeueIdUndSpeichertAbschluss()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var repository = new PeriodCloseRepository(factory);
        var periodClose = MakeClose(Von, Bis, DateTime.UtcNow);
        periodClose.Id = Guid.Empty;

        var added = await repository.AddAsync(periodClose);

        added.Id.Should().NotBe(Guid.Empty);
        (await repository.GetAllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task GetAllAsync_SortiertAbsteigendNachClosedAt()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.PeriodCloses.AddRange(
                MakeClose(Von, Bis, new DateTime(2026, 4, 1)),
                MakeClose(Von.AddYears(-1), Bis.AddYears(-1), new DateTime(2026, 4, 5)));
            await context.SaveChangesAsync();
        }

        var repository = new PeriodCloseRepository(factory);
        var result = await repository.GetAllAsync();

        result.Should().HaveCount(2);
        result[0].ClosedAt.Should().Be(new DateTime(2026, 4, 5));
    }

    [Fact]
    public async Task GetForRangeAsync_KeinEintrag_GibtNullZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var repository = new PeriodCloseRepository(factory);

        (await repository.GetForRangeAsync(Von, Bis)).Should().BeNull();
    }

    [Fact]
    public async Task GetForRangeAsync_MehrereEintraege_GibtNeuestenZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.PeriodCloses.AddRange(
                MakeClose(Von, Bis, new DateTime(2026, 4, 1)),
                MakeClose(Von, Bis, new DateTime(2026, 5, 1)));
            await context.SaveChangesAsync();
        }

        var repository = new PeriodCloseRepository(factory);
        var result = await repository.GetForRangeAsync(Von, Bis);

        result!.ClosedAt.Should().Be(new DateTime(2026, 5, 1));
    }

    [Fact]
    public async Task GetForRangeAsync_AndererZeitraum_GibtNullZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.PeriodCloses.Add(MakeClose(Von, Bis, DateTime.UtcNow));
            await context.SaveChangesAsync();
        }

        var repository = new PeriodCloseRepository(factory);
        (await repository.GetForRangeAsync(Von.AddDays(1), Bis)).Should().BeNull();
    }
}
