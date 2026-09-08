using FluentAssertions;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Kuestencode.Werkbank.Saldo.Domain.Enums;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Data;

public class KontoRepositoryTests
{
    private static Konto MakeKonto(string kontenrahmen, string nummer, string bezeichnung = "Konto", KontoTyp typ = KontoTyp.Ausgabe) => new()
    {
        Id = Guid.NewGuid(),
        Kontenrahmen = kontenrahmen,
        KontoNummer = nummer,
        KontoBezeichnung = bezeichnung,
        KontoTyp = typ
    };

    [Fact]
    public async Task GetAllAsync_OhneFilter_GibtAlleKontenSortiertNachNummerZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.AddRange(
                MakeKonto("SKR03", "4200"),
                MakeKonto("SKR03", "4000"));
            await context.SaveChangesAsync();
        }

        var repository = new KontoRepository(factory);
        var result = await repository.GetAllAsync();

        result.Should().HaveCount(2);
        result[0].KontoNummer.Should().Be("4000");
        result[1].KontoNummer.Should().Be("4200");
    }

    [Fact]
    public async Task GetAllAsync_MitKontenrahmenFilter_GibtNurPassendeKontenZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.AddRange(
                MakeKonto("SKR03", "4000"),
                MakeKonto("SKR04", "4000"));
            await context.SaveChangesAsync();
        }

        var repository = new KontoRepository(factory);
        var result = await repository.GetAllAsync("SKR04");

        result.Should().ContainSingle(k => k.Kontenrahmen == "SKR04");
    }

    [Fact]
    public async Task GetByNummerAsync_FindetKontoAnhandKontenrahmenUndNummer()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var konto = MakeKonto("SKR03", "4980", "Sonstige betriebliche Aufwendungen");
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.Add(konto);
            await context.SaveChangesAsync();
        }

        var repository = new KontoRepository(factory);
        var result = await repository.GetByNummerAsync("SKR03", "4980");

        result.Should().NotBeNull();
        result!.Id.Should().Be(konto.Id);
    }

    [Fact]
    public async Task GetByNummerAsync_AndererKontenrahmen_GibtNullZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.Add(MakeKonto("SKR03", "4980"));
            await context.SaveChangesAsync();
        }

        var repository = new KontoRepository(factory);
        var result = await repository.GetByNummerAsync("SKR04", "4980");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByKontenrahmenAsync_GibtNurKontenDesKontenrahmensZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.AddRange(
                MakeKonto("SKR03", "1000"),
                MakeKonto("SKR04", "1000"));
            await context.SaveChangesAsync();
        }

        var repository = new KontoRepository(factory);
        var result = await repository.GetByKontenrahmenAsync("SKR03");

        result.Should().ContainSingle(k => k.Kontenrahmen == "SKR03");
    }

    [Fact]
    public async Task ExistsAsync_VorhandenesKonto_GibtTrueZurueck()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.Add(MakeKonto("SKR03", "8400"));
            await context.SaveChangesAsync();
        }

        var repository = new KontoRepository(factory);
        (await repository.ExistsAsync("SKR03", "8400")).Should().BeTrue();
        (await repository.ExistsAsync("SKR03", "9999")).Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_SpeichertNeuesKonto()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var repository = new KontoRepository(factory);

        var added = await repository.AddAsync(MakeKonto("SKR03", "1200", "Bank"));

        (await repository.GetByIdAsync(added.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_AktualisiertBestehendesKonto()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var konto = MakeKonto("SKR03", "1200", "Bank");
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.Add(konto);
            await context.SaveChangesAsync();
        }

        var repository = new KontoRepository(factory);
        konto.KontoBezeichnung = "Bankkonto Hauptgeschäft";
        await repository.UpdateAsync(konto);

        var updated = await repository.GetByIdAsync(konto.Id);
        updated!.KontoBezeichnung.Should().Be("Bankkonto Hauptgeschäft");
    }

    [Fact]
    public async Task AddRangeAsync_FuegtMehrereKontenHinzu()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var repository = new KontoRepository(factory);

        await repository.AddRangeAsync([MakeKonto("SKR03", "1"), MakeKonto("SKR03", "2")]);

        (await repository.GetAllAsync()).Should().HaveCount(2);
    }
}
