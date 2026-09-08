using FluentAssertions;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Data;

public class KontoMappingOverrideRepositoryTests
{
    [Fact]
    public async Task UpsertAsync_KeinVorhandenerEintrag_LegtNeuenOverrideAn()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());

        var result = await repository.UpsertAsync("SKR03", "Buerobedarf", "4930");

        result.KontoNummer.Should().Be("4930");
        result.Kategorie.Should().Be("Buerobedarf");
    }

    [Fact]
    public async Task UpsertAsync_VorhandenerEintrag_AktualisiertKontoNummerStattNeuAnzulegen()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());
        var first = await repository.UpsertAsync("SKR03", "Buerobedarf", "4930");

        var updated = await repository.UpsertAsync("SKR03", "Buerobedarf", "4940");

        updated.Id.Should().Be(first.Id);
        updated.KontoNummer.Should().Be("4940");
        (await repository.GetAllAsync("SKR03")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetByKategorieAsync_KeinEintrag_GibtNullZurueck()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());

        (await repository.GetByKategorieAsync("SKR03", "Unbekannt")).Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_FiltertNachKontenrahmenUndSortiertNachKategorie()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());
        await repository.UpsertAsync("SKR03", "Zubehoer", "4900");
        await repository.UpsertAsync("SKR03", "Ausruestung", "4910");
        await repository.UpsertAsync("SKR04", "Zubehoer", "4900");

        var result = await repository.GetAllAsync("SKR03");

        result.Should().HaveCount(2);
        result[0].Kategorie.Should().Be("Ausruestung");
        result[1].Kategorie.Should().Be("Zubehoer");
    }

    [Fact]
    public async Task DeleteAsync_VorhandenerEintrag_WirdEntfernt()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());
        await repository.UpsertAsync("SKR03", "Buerobedarf", "4930");

        await repository.DeleteAsync("SKR03", "Buerobedarf");

        (await repository.GetByKategorieAsync("SKR03", "Buerobedarf")).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnbekannterEintrag_WirftKeineException()
    {
        var repository = new KontoMappingOverrideRepository(TestDbContextFactory.CreateInMemory());

        var act = () => repository.DeleteAsync("SKR03", "Unbekannt");

        await act.Should().NotThrowAsync();
    }
}
