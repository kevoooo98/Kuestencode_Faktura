using FluentAssertions;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Data;

public class KategorieKontoMappingRepositoryTests
{
    private static KategorieKontoMapping MakeMapping(string kontenrahmen, string kategorie, string kontoNummer) => new()
    {
        Id = Guid.NewGuid(),
        Kontenrahmen = kontenrahmen,
        ReceiptaKategorie = kategorie,
        KontoNummer = kontoNummer
    };

    // KategorieKontoMapping referenziert Konto per (Kontenrahmen, KontoNummer) als required Fremdschlüssel —
    // ohne passendes Konto liefert .Include(m => m.Konto) die Zeile gar nicht erst zurück.
    private static Konto MakeKonto(string kontenrahmen, string kontoNummer) => new()
    {
        Id = Guid.NewGuid(),
        Kontenrahmen = kontenrahmen,
        KontoNummer = kontoNummer,
        KontoBezeichnung = "Konto " + kontoNummer
    };

    [Fact]
    public async Task GetAllAsync_MitKontenrahmenFilter_SortiertNachKategorie()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.AddRange(MakeKonto("SKR03", "4900"), MakeKonto("SKR03", "4910"), MakeKonto("SKR04", "4900"));
            context.KategorieKontoMappings.AddRange(
                MakeMapping("SKR03", "Zubehoer", "4900"),
                MakeMapping("SKR03", "Ausruestung", "4910"),
                MakeMapping("SKR04", "Zubehoer", "4900"));
            await context.SaveChangesAsync();
        }

        var repository = new KategorieKontoMappingRepository(factory);
        var result = await repository.GetAllAsync("SKR03");

        result.Should().HaveCount(2);
        result[0].ReceiptaKategorie.Should().Be("Ausruestung");
    }

    [Fact]
    public async Task GetByKategorieAsync_FindetPassendesMapping()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var mapping = MakeMapping("SKR03", "Buerobedarf", "4930");
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.Add(MakeKonto("SKR03", "4930"));
            context.KategorieKontoMappings.Add(mapping);
            await context.SaveChangesAsync();
        }

        var repository = new KategorieKontoMappingRepository(factory);
        var result = await repository.GetByKategorieAsync("SKR03", "Buerobedarf");

        result.Should().NotBeNull();
        result!.KontoNummer.Should().Be("4930");
    }

    [Fact]
    public async Task ExistsAsync_PrueftKontenrahmenUndKategorieGemeinsam()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.KategorieKontoMappings.Add(MakeMapping("SKR03", "Buerobedarf", "4930"));
            await context.SaveChangesAsync();
        }

        var repository = new KategorieKontoMappingRepository(factory);

        (await repository.ExistsAsync("SKR03", "Buerobedarf")).Should().BeTrue();
        (await repository.ExistsAsync("SKR04", "Buerobedarf")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_AktualisiertKontoNummerDesMappings()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var mappingId = Guid.NewGuid();
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.AddRange(MakeKonto("SKR03", "4930"), MakeKonto("SKR03", "4940"));
            context.KategorieKontoMappings.Add(new KategorieKontoMapping
            {
                Id = mappingId, Kontenrahmen = "SKR03", ReceiptaKategorie = "Buerobedarf", KontoNummer = "4930"
            });
            await context.SaveChangesAsync();
        }

        // Detachtes Objekt ohne Konto-Navigation übergeben, damit EFs Fixup-Logik die
        // Fremdschlüssel-Änderung nicht anhand einer (bereits geladenen) alten Navigation zurücksetzt.
        var repository = new KategorieKontoMappingRepository(factory);
        await repository.UpdateAsync(new KategorieKontoMapping
        {
            Id = mappingId, Kontenrahmen = "SKR03", ReceiptaKategorie = "Buerobedarf", KontoNummer = "4940"
        });

        var updated = await repository.GetByIdAsync(mappingId);
        updated!.KontoNummer.Should().Be("4940");
    }

    [Fact]
    public async Task AddRangeAsync_FuegtMehrereMappingsHinzu()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        await using (var context = factory.CreateDbContext())
        {
            context.Konten.AddRange(MakeKonto("SKR03", "1"), MakeKonto("SKR03", "2"));
            await context.SaveChangesAsync();
        }

        var repository = new KategorieKontoMappingRepository(factory);
        await repository.AddRangeAsync(
        [
            MakeMapping("SKR03", "A", "1"),
            MakeMapping("SKR03", "B", "2")
        ]);

        (await repository.GetAllAsync("SKR03")).Should().HaveCount(2);
    }
}
