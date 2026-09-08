using FluentAssertions;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Data;

public class SaldoSettingsRepositoryTests
{
    [Fact]
    public async Task GetAsync_KeineEinstellungenVorhanden_GibtNullZurueck()
    {
        var repository = new SaldoSettingsRepository(TestDbContextFactory.CreateInMemory());

        (await repository.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_VergibtNeueIdUndSpeichertEinstellungen()
    {
        var repository = new SaldoSettingsRepository(TestDbContextFactory.CreateInMemory());
        var settings = new SaldoSettings { Kontenrahmen = "SKR03", WirtschaftsjahrBeginn = 1 };

        var created = await repository.CreateAsync(settings);

        created.Id.Should().NotBe(Guid.Empty);
        (await repository.GetAsync())!.Kontenrahmen.Should().Be("SKR03");
    }

    [Fact]
    public async Task UpdateAsync_AktualisiertBestehendeEinstellungen()
    {
        var factory = TestDbContextFactory.CreateInMemory();
        var repository = new SaldoSettingsRepository(factory);
        var created = await repository.CreateAsync(new SaldoSettings { Kontenrahmen = "SKR03" });

        created.Kontenrahmen = "SKR04";
        await repository.UpdateAsync(created);

        (await repository.GetAsync())!.Kontenrahmen.Should().Be("SKR04");
    }
}
