using FluentAssertions;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Data;

public class KontoMappingOverrideRepositoryTests
{
    private static readonly DateOnly Heute = new(2026, 9, 10);

    [Fact]
    public async Task SetOverrideAsync_KeinVorhandenerEintrag_LegtNeuenOverrideAn()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());

        var result = await repository.SetOverrideAsync("SKR03", "Buerobedarf", "4930", Heute);

        result.KontoNummer.Should().Be("4930");
        result.Kategorie.Should().Be("Buerobedarf");
        result.GueltigAb.Should().Be(Heute);
        result.GueltigBis.Should().BeNull();
    }

    [Fact]
    public async Task SetOverrideAsync_VorhandenerOffenerEintrag_SchliesstAltenUndLegtNeuenAn()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());
        var first = await repository.SetOverrideAsync("SKR03", "Buerobedarf", "4930", Heute);

        var spaeter = Heute.AddDays(10);
        var updated = await repository.SetOverrideAsync("SKR03", "Buerobedarf", "4940", spaeter);

        updated.Id.Should().NotBe(first.Id);
        updated.KontoNummer.Should().Be("4940");
        updated.GueltigAb.Should().Be(spaeter);
        updated.GueltigBis.Should().BeNull();

        // beide Versionen bleiben erhalten (Historie)
        var alle = await repository.GetAllAsync("SKR03");
        alle.Should().ContainSingle(); // GetAllAsync liefert nur die offene Version
        alle[0].Id.Should().Be(updated.Id);
    }

    [Fact]
    public async Task GetByKategorieAsync_VergangenerZeitraum_LiefertDamalsGueltigeVersion()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());
        await repository.SetOverrideAsync("SKR03", "Buerobedarf", "4930", Heute);
        await repository.SetOverrideAsync("SKR03", "Buerobedarf", "4940", Heute.AddDays(10));

        // Stichtag vor der Änderung -> alte Kontonummer, unabhängig von der späteren Änderung
        var damals = await repository.GetByKategorieAsync("SKR03", "Buerobedarf", Heute.AddDays(5));
        damals!.KontoNummer.Should().Be("4930");

        var heute = await repository.GetByKategorieAsync("SKR03", "Buerobedarf", Heute.AddDays(10));
        heute!.KontoNummer.Should().Be("4940");
    }

    [Fact]
    public async Task GetByKategorieAsync_KeinEintrag_GibtNullZurueck()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());

        (await repository.GetByKategorieAsync("SKR03", "Unbekannt", Heute)).Should().BeNull();
    }

    [Fact]
    public async Task GetByKategorieAsync_StichtagVorGueltigAb_GibtNullZurueck()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());
        await repository.SetOverrideAsync("SKR03", "Buerobedarf", "4930", Heute);

        (await repository.GetByKategorieAsync("SKR03", "Buerobedarf", Heute.AddDays(-1))).Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_FiltertNachKontenrahmenUndSortiertNachKategorie()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());
        await repository.SetOverrideAsync("SKR03", "Zubehoer", "4900", Heute);
        await repository.SetOverrideAsync("SKR03", "Ausruestung", "4910", Heute);
        await repository.SetOverrideAsync("SKR04", "Zubehoer", "4900", Heute);

        var result = await repository.GetAllAsync("SKR03");

        result.Should().HaveCount(2);
        result[0].Kategorie.Should().Be("Ausruestung");
        result[1].Kategorie.Should().Be("Zubehoer");
    }

    [Fact]
    public async Task DeleteAsync_VorhandenerEintrag_SchliesstOffeneVersionAbStichtag()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());
        await repository.SetOverrideAsync("SKR03", "Buerobedarf", "4930", Heute);

        await repository.DeleteAsync("SKR03", "Buerobedarf", Heute.AddDays(5));

        // ab dem Stichtag gilt kein Override mehr ...
        (await repository.GetByKategorieAsync("SKR03", "Buerobedarf", Heute.AddDays(5))).Should().BeNull();
        // ... aber die Historie für den davorliegenden Zeitraum bleibt unverändert
        (await repository.GetByKategorieAsync("SKR03", "Buerobedarf", Heute.AddDays(4)))!.KontoNummer.Should().Be("4930");
    }

    [Fact]
    public async Task DeleteAsync_UnbekannterEintrag_WirftKeineException()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());

        var act = () => repository.DeleteAsync("SKR03", "Unbekannt", Heute);

        await act.Should().NotThrowAsync();
    }
}
