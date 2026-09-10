using FluentAssertions;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Dtos;
using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Kuestencode.Werkbank.Saldo.Domain.Enums;
using Kuestencode.Werkbank.Saldo.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Services;

public class KontoMappingServiceTests
{
    private readonly Mock<IKontoMappingOverrideRepository> _overrideRepo = new();
    private readonly Mock<IKategorieKontoMappingRepository> _mappingRepo = new();
    private readonly Mock<IKontoRepository> _kontoRepo = new();
    private readonly Mock<ISaldoSettingsRepository> _settingsRepo = new();

    private KontoMappingService CreateService() =>
        new(_overrideRepo.Object, _mappingRepo.Object, _kontoRepo.Object,
            _settingsRepo.Object, NullLogger<KontoMappingService>.Instance);

    private void SetupKontenrahmen(string kontenrahmen = "SKR03")
    {
        _settingsRepo.Setup(r => r.GetAsync())
            .ReturnsAsync(new SaldoSettings { Kontenrahmen = kontenrahmen });
    }

    // ─── GetEinnahmenKontoAsync ───────────────────────────────────────────────

    [Theory]
    [InlineData(19, "SKR03", "8400")]
    [InlineData(7,  "SKR03", "8300")]
    [InlineData(0,  "SKR03", "8120")]
    [InlineData(19, "SKR04", "4400")]
    [InlineData(7,  "SKR04", "4300")]
    [InlineData(0,  "SKR04", "4120")]
    public async Task GetEinnahmenKonto_FallbackWennKeinKontoInDB(decimal ustSatz, string kontenrahmen, string erwartetesKonto)
    {
        SetupKontenrahmen(kontenrahmen);
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync(kontenrahmen))
            .ReturnsAsync(new List<Konto>()); // leer → Fallback

        var service = CreateService();
        var result = await service.GetEinnahmenKontoAsync(ustSatz);

        result.Should().Be(erwartetesKonto);
    }

    [Fact]
    public async Task GetEinnahmenKonto_NutztDatenbankKontoWennVorhanden()
    {
        SetupKontenrahmen("SKR03");
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03"))
            .ReturnsAsync(new List<Konto>
            {
                new() { KontoNummer = "8410", KontoTyp = KontoTyp.Einnahme, UstSatz = 19 }
            });

        var service = CreateService();
        var result = await service.GetEinnahmenKontoAsync(19);

        result.Should().Be("8410");
    }

    // ─── GetAusgabenKontoAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetAusgabenKonto_NutztOverrideAlsErstes()
    {
        const string kategorie = "Büromaterial";
        SetupKontenrahmen("SKR03");

        _overrideRepo.Setup(r => r.GetByKategorieAsync("SKR03", kategorie, It.IsAny<DateOnly>()))
            .ReturnsAsync(new KontoMappingOverride { KontoNummer = "4950" });
        _mappingRepo.Setup(r => r.GetByKategorieAsync("SKR03", kategorie))
            .ReturnsAsync(new KategorieKontoMapping { KontoNummer = "4930" });

        var service = CreateService();
        var result = await service.GetAusgabenKontoAsync(kategorie);

        result.Should().Be("4950"); // Override hat Vorrang
    }

    [Fact]
    public async Task GetAusgabenKonto_NutztStandardMappingWennKeinOverride()
    {
        const string kategorie = "Reisekosten";
        SetupKontenrahmen("SKR03");

        _overrideRepo.Setup(r => r.GetByKategorieAsync("SKR03", kategorie, It.IsAny<DateOnly>()))
            .ReturnsAsync((KontoMappingOverride?)null);
        _mappingRepo.Setup(r => r.GetByKategorieAsync("SKR03", kategorie))
            .ReturnsAsync(new KategorieKontoMapping { KontoNummer = "4660" });

        var service = CreateService();
        var result = await service.GetAusgabenKontoAsync(kategorie);

        result.Should().Be("4660");
    }

    [Theory]
    [InlineData("SKR03", "4900")]
    [InlineData("SKR04", "6300")]
    public async Task GetAusgabenKonto_FallbackWennKeinMappingUndKeinOverride(string kontenrahmen, string erwartetesKonto)
    {
        const string kategorie = "UnbekanntKategorie";
        SetupKontenrahmen(kontenrahmen);

        _overrideRepo.Setup(r => r.GetByKategorieAsync(kontenrahmen, kategorie, It.IsAny<DateOnly>()))
            .ReturnsAsync((KontoMappingOverride?)null);
        _mappingRepo.Setup(r => r.GetByKategorieAsync(kontenrahmen, kategorie))
            .ReturnsAsync((KategorieKontoMapping?)null);

        var service = CreateService();
        var result = await service.GetAusgabenKontoAsync(kategorie);

        result.Should().Be(erwartetesKonto);
    }

    [Fact]
    public async Task GetAusgabenKonto_MitStichtag_ReichtStichtagAnRepositoryDurch()
    {
        const string kategorie = "Büromaterial";
        var stichtag = new DateOnly(2026, 1, 1);
        SetupKontenrahmen("SKR03");

        _overrideRepo.Setup(r => r.GetByKategorieAsync("SKR03", kategorie, stichtag))
            .ReturnsAsync(new KontoMappingOverride { KontoNummer = "4950" });

        var service = CreateService();
        var result = await service.GetAusgabenKontoAsync(kategorie, stichtag);

        result.Should().Be("4950");
        _overrideRepo.Verify(r => r.GetByKategorieAsync("SKR03", kategorie, stichtag), Times.Once);
    }

    // ─── GetBankKontoAsync ────────────────────────────────────────────────────

    [Theory]
    [InlineData("SKR03", "1200")]
    [InlineData("SKR04", "1800")]
    public async Task GetBankKonto_FallbackWennKeinBankkontoInDB(string kontenrahmen, string erwartetesKonto)
    {
        SetupKontenrahmen(kontenrahmen);
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync(kontenrahmen))
            .ReturnsAsync(new List<Konto>()); // leer → Fallback

        var service = CreateService();
        var result = await service.GetBankKontoAsync();

        result.Should().Be(erwartetesKonto);
    }

    [Fact]
    public async Task GetBankKonto_NutztDatenbankKontoWennVorhanden()
    {
        SetupKontenrahmen("SKR03");
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03"))
            .ReturnsAsync(new List<Konto>
            {
                new() { KontoNummer = "1210", KontoTyp = KontoTyp.Bank }
            });

        var service = CreateService();
        var result = await service.GetBankKontoAsync();

        result.Should().Be("1210");
    }

    // ─── GetResolvedMappingsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetResolvedMappings_MarksOverridesKorrekt()
    {
        const string kontenrahmen = "SKR03";
        _mappingRepo.Setup(r => r.GetAllAsync(kontenrahmen))
            .ReturnsAsync(new List<KategorieKontoMapping>
            {
                new() { ReceiptaKategorie = "Büromaterial", KontoNummer = "4930" },
                new() { ReceiptaKategorie = "Reisekosten",  KontoNummer = "4660" }
            });
        _overrideRepo.Setup(r => r.GetAllAsync(kontenrahmen))
            .ReturnsAsync(new List<KontoMappingOverride>
            {
                new() { Kategorie = "Büromaterial", KontoNummer = "4950" }
            });
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync(kontenrahmen))
            .ReturnsAsync(new List<Konto>
            {
                new() { KontoNummer = "4950", KontoBezeichnung = "Bürobedarf (custom)" },
                new() { KontoNummer = "4660", KontoBezeichnung = "Reisekosten" }
            });

        var service = CreateService();
        var result = await service.GetResolvedMappingsAsync(kontenrahmen);

        result.Should().HaveCount(2);

        var buero = result.Single(r => r.Kategorie == "Büromaterial");
        buero.IsOverride.Should().BeTrue();
        buero.KontoNummer.Should().Be("4950");

        var reise = result.Single(r => r.Kategorie == "Reisekosten");
        reise.IsOverride.Should().BeFalse();
        reise.KontoNummer.Should().Be("4660");
    }

    // ─── UpdateMappingAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMapping_WirftExceptionWennKontoNichtImKontenrahmen()
    {
        SetupKontenrahmen("SKR03");
        _kontoRepo.Setup(r => r.ExistsAsync("SKR03", "9999"))
            .ReturnsAsync(false);

        var service = CreateService();
        var act = () => service.UpdateMappingAsync("Sonstiges", "9999");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*9999*");
    }

    [Fact]
    public async Task UpdateMapping_ErstelltOverrideWennKontoExistiert()
    {
        const string kategorie = "Sonstiges";
        const string kontoNummer = "4980";
        SetupKontenrahmen("SKR03");

        _kontoRepo.Setup(r => r.ExistsAsync("SKR03", kontoNummer))
            .ReturnsAsync(true);
        _overrideRepo.Setup(r => r.SetOverrideAsync("SKR03", kategorie, kontoNummer, It.IsAny<DateOnly>()))
            .ReturnsAsync(new KontoMappingOverride
            {
                Kategorie = kategorie,
                KontoNummer = kontoNummer,
                Kontenrahmen = "SKR03"
            });

        var service = CreateService();
        var result = await service.UpdateMappingAsync(kategorie, kontoNummer);

        result.KontoNummer.Should().Be(kontoNummer);
        result.Kategorie.Should().Be(kategorie);
        _overrideRepo.Verify(r => r.SetOverrideAsync("SKR03", kategorie, kontoNummer, It.IsAny<DateOnly>()), Times.Once);
    }

    // ─── ResetMappingAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ResetMapping_LoeschtOverride()
    {
        const string kategorie = "Büromaterial";
        SetupKontenrahmen("SKR03");
        _overrideRepo.Setup(r => r.DeleteAsync("SKR03", kategorie, It.IsAny<DateOnly>())).Returns(Task.CompletedTask);

        var service = CreateService();
        await service.ResetMappingAsync(kategorie);

        _overrideRepo.Verify(r => r.DeleteAsync("SKR03", kategorie, It.IsAny<DateOnly>()), Times.Once);
    }

    // ─── GetAlleKontenAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAlleKonten_GibtAlleKontenAlsDtoZurueck()
    {
        const string kontenrahmen = "SKR03";
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync(kontenrahmen))
            .ReturnsAsync(new List<Konto>
            {
                new() { Id = Guid.NewGuid(), Kontenrahmen = kontenrahmen, KontoNummer = "8400", KontoBezeichnung = "Erlöse 19%", KontoTyp = KontoTyp.Einnahme, UstSatz = 19, IsActive = true },
                new() { Id = Guid.NewGuid(), Kontenrahmen = kontenrahmen, KontoNummer = "1200", KontoBezeichnung = "Bank",       KontoTyp = KontoTyp.Bank,     IsActive = true }
            });

        var service = CreateService();
        var result = await service.GetAlleKontenAsync(kontenrahmen);

        result.Should().HaveCount(2);
        result[0].KontoNummer.Should().Be("8400");
        result[0].KontoTyp.Should().Be("Einnahme");
        result[0].UstSatz.Should().Be(19);
        result[1].KontoNummer.Should().Be("1200");
        result[1].KontoTyp.Should().Be("Bank");
    }

    [Fact]
    public async Task GetAlleKonten_GibtLeereListeWennKeineKontenVorhanden()
    {
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03")).ReturnsAsync(new List<Konto>());

        var service = CreateService();
        var result = await service.GetAlleKontenAsync("SKR03");

        result.Should().BeEmpty();
    }

    // ─── GetOverridesAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetOverrides_GibtAlleOverridesAlsDtoZurueck()
    {
        SetupKontenrahmen("SKR03");
        _overrideRepo.Setup(r => r.GetAllAsync("SKR03"))
            .ReturnsAsync(new List<KontoMappingOverride>
            {
                new() { Id = Guid.NewGuid(), Kontenrahmen = "SKR03", Kategorie = "Büromaterial", KontoNummer = "4950" },
                new() { Id = Guid.NewGuid(), Kontenrahmen = "SKR03", Kategorie = "Reise",        KontoNummer = "4660" }
            });

        var service = CreateService();
        var result = await service.GetOverridesAsync();

        result.Should().HaveCount(2);
        result[0].Kategorie.Should().Be("Büromaterial");
        result[0].KontoNummer.Should().Be("4950");
        result[1].Kategorie.Should().Be("Reise");
    }

    [Fact]
    public async Task GetOverrides_GibtLeereListeWennKeineOverridesVorhanden()
    {
        SetupKontenrahmen("SKR03");
        _overrideRepo.Setup(r => r.GetAllAsync("SKR03")).ReturnsAsync(new List<KontoMappingOverride>());

        var service = CreateService();
        var result = await service.GetOverridesAsync();

        result.Should().BeEmpty();
    }
}
