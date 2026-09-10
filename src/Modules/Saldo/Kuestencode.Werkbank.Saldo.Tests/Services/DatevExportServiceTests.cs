using FluentAssertions;
using Kuestencode.Core.Interfaces;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Faktura;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Dtos;
using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Kuestencode.Werkbank.Saldo.Domain.Enums;
using Kuestencode.Werkbank.Saldo.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Services;

public class DatevExportServiceTests
{
    private readonly Mock<ISaldoAggregationService> _saldoService = new();
    private readonly Mock<ISaldoSettingsRepository> _settingsRepo = new();
    private readonly Mock<IKontoMappingService> _kontoMappingService = new();
    private readonly Mock<IFakturaApiClient> _fakturaClient = new();
    private readonly Mock<IReceptaApiClient> _receptaClient = new();
    private readonly Mock<IReceptaDataService> _receptaDataService = new();
    private readonly Mock<IExportLogRepository> _exportLogRepo = new();
    private readonly Mock<ICompanyService> _companyService = new();

    private static readonly DateOnly Von = new(2026, 1, 1);
    private static readonly DateOnly Bis = new(2026, 12, 31);
    private static readonly Guid TestUserId = Guid.NewGuid();

    private DatevExportService CreateService() =>
        new(_saldoService.Object, _settingsRepo.Object, _kontoMappingService.Object,
            _fakturaClient.Object, _receptaClient.Object, _receptaDataService.Object,
            _exportLogRepo.Object, _companyService.Object, NullLogger<DatevExportService>.Instance);

    private void SetupBasicMocks()
    {
        _settingsRepo.Setup(r => r.GetAsync())
            .ReturnsAsync(new SaldoSettings
            {
                Kontenrahmen = "SKR03",
                BeraterNummer = "12345",
                MandantenNummer = "67890"
            });
        _kontoMappingService.Setup(s => s.GetBankKontoAsync()).ReturnsAsync("1200");
        _exportLogRepo.Setup(r => r.AddAsync(It.IsAny<ExportLog>())).ReturnsAsync(new ExportLog());
    }

    // ─── Encoding ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportBuchungsstapel_IstWindows1252Encoded()
    {
        SetupBasicMocks();
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>());

        var service = CreateService();
        var result = await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        // Erster Byte-Check: muss mit EXTF in Windows-1252 beginnen
        var windows1252 = Encoding.GetEncoding(1252);
        var text = windows1252.GetString(result);
        text.Should().StartWith("\"EXTF\"");
    }

    [Fact]
    public async Task ExportBuchungsstapel_EnthaltDatevHeader()
    {
        SetupBasicMocks();
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>());

        var service = CreateService();
        var result = await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        var text = Encoding.GetEncoding(1252).GetString(result);
        var zeilen = text.Split('\n');

        // Zeile 1: DATEV-Header
        zeilen[0].Should().StartWith("\"EXTF\";700;21;\"Buchungsstapel\"");

        // Zeile 2: Spaltenüberschriften
        zeilen[1].Should().Contain("Umsatz");
        zeilen[1].Should().Contain("Konto");
        zeilen[1].Should().Contain("BU-Schlüssel");
    }

    // ─── Buchungszeilen ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExportBuchungsstapel_EinnahmeHatSollKennzeichen()
    {
        SetupBasicMocks();
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>
            {
                new()
                {
                    Typ = BuchungsTyp.Einnahme,
                    Brutto = 1190m,
                    UstSatz = 19,
                    ZahlungsDatum = new DateOnly(2026, 3, 15),
                    QuelleId = "RE-2026-001",
                    Beschreibung = "Kunde XY",
                    KontoNummer = "8400"
                }
            });

        var service = CreateService();
        var result = await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        var text = Encoding.GetEncoding(1252).GetString(result);
        var buchungszeile = text.Split('\n')[2]; // Zeile 3

        buchungszeile.Should().Contain("\"S\"");    // Soll für Einnahme
        buchungszeile.Should().Contain("1190,00");  // Betrag mit Komma
        buchungszeile.Should().Contain("1200");     // Bankkonto
        buchungszeile.Should().Contain("8400");     // Erlöskonto
    }

    [Fact]
    public async Task ExportBuchungsstapel_AusgabeHatHabenKennzeichen()
    {
        SetupBasicMocks();
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>
            {
                new()
                {
                    Typ = BuchungsTyp.Ausgabe,
                    Brutto = 357m,
                    UstSatz = 19,
                    ZahlungsDatum = new DateOnly(2026, 4, 1),
                    QuelleId = "ER-001",
                    Beschreibung = "Lieferant AG",
                    KontoNummer = "4980"
                }
            });

        var service = CreateService();
        var result = await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        var text = Encoding.GetEncoding(1252).GetString(result);
        var buchungszeile = text.Split('\n')[2];

        buchungszeile.Should().Contain("\"H\""); // Haben für Ausgabe
        buchungszeile.Should().Contain("357,00");
        buchungszeile.Should().Contain("4980");
    }

    [Fact]
    public async Task ExportBuchungsstapel_NegativeEinnahmeKehrtSollHabenUmUndBleibtOhneVorzeichen()
    {
        SetupBasicMocks();
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>
            {
                new()
                {
                    Typ = BuchungsTyp.Einnahme,
                    Brutto = -1190m, // Gutschrift: negative Einnahme
                    UstSatz = 19,
                    ZahlungsDatum = new DateOnly(2026, 3, 20),
                    QuelleId = "GS-2026-001",
                    Beschreibung = "Gutschrift Kunde XY",
                    KontoNummer = "8400"
                }
            });

        var service = CreateService();
        var result = await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        var text = Encoding.GetEncoding(1252).GetString(result);
        var buchungszeile = text.Split('\n')[2];

        buchungszeile.Should().Contain("\"H\""); // Gutschrift kippt Einnahme von Soll auf Haben
        buchungszeile.Should().Contain("1190,00"); // Umsatzfeld ohne Vorzeichen
        buchungszeile.Should().NotContain("-1190,00");
    }

    // ─── BU-Schlüssel ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BuchungsTyp.Einnahme, 19, "3")]
    [InlineData(BuchungsTyp.Einnahme, 7,  "2")]
    [InlineData(BuchungsTyp.Einnahme, 0,  "0")]
    [InlineData(BuchungsTyp.Ausgabe,  19, "9")]
    [InlineData(BuchungsTyp.Ausgabe,  7,  "8")]
    [InlineData(BuchungsTyp.Ausgabe,  0,  "0")]
    public async Task ExportBuchungsstapel_BuSchluesselKorrektNachSteuersatz(
        BuchungsTyp typ, decimal ustSatz, string erwarteteBu)
    {
        SetupBasicMocks();
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>
            {
                new()
                {
                    Typ = typ,
                    Brutto = 100m,
                    UstSatz = ustSatz,
                    ZahlungsDatum = Von,
                    KontoNummer = "8400"
                }
            });

        var service = CreateService();
        var result = await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        var text = Encoding.GetEncoding(1252).GetString(result);
        var buchungszeile = text.Split('\n')[2];

        // BU-Schlüssel ist das 6. Feld (0-basiert: Index 5)
        var felder = buchungszeile.Split(';');
        felder[5].Should().Be(erwarteteBu);
    }

    // ─── Belegdatum-Format ────────────────────────────────────────────────────

    [Fact]
    public async Task ExportBuchungsstapel_BelegdatumImDatevFormat()
    {
        SetupBasicMocks();
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>
            {
                new()
                {
                    Typ = BuchungsTyp.Einnahme,
                    Brutto = 100m,
                    ZahlungsDatum = new DateOnly(2026, 3, 5), // 05.03.2026
                    KontoNummer = "8400"
                }
            });

        var service = CreateService();
        var result = await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        var text = Encoding.GetEncoding(1252).GetString(result);
        var buchungszeile = text.Split('\n')[2];

        // Belegdatum: TTMMJJJJ
        buchungszeile.Should().Contain("\"05032026\"");
    }

    // ─── Sonderzeichen-Bereinigung ────────────────────────────────────────────

    [Fact]
    public async Task ExportBuchungsstapel_SemikolonInBeschreibungWirdErsetzt()
    {
        SetupBasicMocks();
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>
            {
                new()
                {
                    Typ = BuchungsTyp.Einnahme,
                    Brutto = 100m,
                    ZahlungsDatum = Von,
                    Beschreibung = "Kunde; Test",
                    KontoNummer = "8400"
                }
            });

        var service = CreateService();
        var result = await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        var text = Encoding.GetEncoding(1252).GetString(result);

        // Semikolon in Beschreibung muss durch Komma ersetzt worden sein
        text.Should().Contain("Kunde, Test");
        text.Should().NotContain("Kunde; Test");
    }

    [Fact]
    public async Task ExportBuchungsstapel_BeschreibungWirdAuf60ZeichenGekuerzt()
    {
        SetupBasicMocks();
        var langeBeschreibung = new string('A', 100); // 100 Zeichen
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>
            {
                new()
                {
                    Typ = BuchungsTyp.Einnahme,
                    Brutto = 100m,
                    ZahlungsDatum = Von,
                    Beschreibung = langeBeschreibung,
                    KontoNummer = "8400"
                }
            });

        var service = CreateService();
        var result = await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        var text = Encoding.GetEncoding(1252).GetString(result);
        var buchungszeile = text.Split('\n')[2];

        // Buchungstext (letztes Feld) darf max. 60 Zeichen enthalten
        var felder = buchungszeile.TrimEnd('\r').Split(';');
        var buchungstext = felder[^1].Trim('"');
        buchungstext.Should().HaveLength(60);
    }

    // ─── Export-Historie ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExportBuchungsstapel_LoggtExport()
    {
        SetupBasicMocks();
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>());

        var service = CreateService();
        await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        _exportLogRepo.Verify(r => r.AddAsync(It.Is<ExportLog>(log =>
            log.ExportTyp == ExportTyp.DatevBuchungsstapel &&
            log.ZeitraumVon == Von &&
            log.ZeitraumBis == Bis &&
            log.ExportedByUserId == TestUserId
        )), Times.Once);
    }

    // ─── Dezimaltrennzeichen ──────────────────────────────────────────────────

    [Fact]
    public async Task ExportBuchungsstapel_NutztKommaAlsDezmialtrennzeichen()
    {
        SetupBasicMocks();
        _saldoService.Setup(s => s.GetAlleBuchungenAsync(Von, Bis))
            .ReturnsAsync(new List<BuchungDto>
            {
                new()
                {
                    Typ = BuchungsTyp.Einnahme,
                    Brutto = 1234.56m,
                    ZahlungsDatum = Von,
                    KontoNummer = "8400"
                }
            });

        var service = CreateService();
        var result = await service.ExportBuchungsstapelAsync(Von, Bis, TestUserId);

        var text = Encoding.GetEncoding(1252).GetString(result);
        text.Should().Contain("1234,56");  // Komma, kein Punkt
        text.Should().NotContain("1234.56");
    }

    // ─── Export-Historie ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetLetztenExport_GibtNullZurueckWennKeineExports()
    {
        _exportLogRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ExportLog>());

        var service = CreateService();
        var result = await service.GetLetztenExportAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLetztenExport_GibtErstenEintragZurueck()
    {
        var exportedAt = new DateTime(2026, 3, 15, 10, 0, 0);
        _exportLogRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<ExportLog>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ExportTyp = ExportTyp.DatevBuchungsstapel,
                    ZeitraumVon = Von,
                    ZeitraumBis = Bis,
                    AnzahlBuchungen = 5,
                    DateiName = "DATEV_Buchungsstapel_20260101_20261231.csv",
                    DateiGroesse = 1024,
                    ExportedAt = exportedAt
                }
            });

        var service = CreateService();
        var result = await service.GetLetztenExportAsync();

        result.Should().NotBeNull();
        result!.ExportTyp.Should().Be("DatevBuchungsstapel");
        result.ZeitraumVon.Should().Be(Von);
        result.ZeitraumBis.Should().Be(Bis);
        result.AnzahlBuchungen.Should().Be(5);
        result.DateiName.Should().Be("DATEV_Buchungsstapel_20260101_20261231.csv");
        result.ExportedAt.Should().Be(exportedAt);
    }

    [Fact]
    public async Task GetExportHistorie_GibtAlleExportsAlsDtoZurueck()
    {
        _exportLogRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<ExportLog>
            {
                new() { ExportTyp = ExportTyp.DatevBuchungsstapel, DateiName = "a.csv", ZeitraumVon = Von, ZeitraumBis = Bis },
                new() { ExportTyp = ExportTyp.DatevBelege,         DateiName = "b.zip", ZeitraumVon = Von, ZeitraumBis = Bis }
            });

        var service = CreateService();
        var result = await service.GetExportHistorieAsync();

        result.Should().HaveCount(2);
        result[0].ExportTyp.Should().Be("DatevBuchungsstapel");
        result[1].ExportTyp.Should().Be("DatevBelege");
    }

    [Fact]
    public async Task GetExportHistorie_GibtLeereListeZurueckWennKeineExports()
    {
        _exportLogRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ExportLog>());

        var service = CreateService();
        var result = await service.GetExportHistorieAsync();

        result.Should().BeEmpty();
    }

    // ─── ExportBelegeAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ExportBelege_GibtGueltigesZipZurueck()
    {
        SetupBasicMocks();
        _receptaDataService.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<Kuestencode.Shared.Contracts.Recepta.ReceptaPaymentDto>());
        _fakturaClient.Setup(c => c.GetEuerPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<Kuestencode.Shared.Contracts.Faktura.InvoiceEuerPaymentDto>());

        var service = CreateService();
        var result = await service.ExportBelegeAsync(Von, Bis, TestUserId);

        result.Should().NotBeNull();
        result.Length.Should().BeGreaterThan(0);
        // ZIP beginnt mit PK-Header (50 4B)
        result[0].Should().Be(0x50);
        result[1].Should().Be(0x4B);
    }

    [Fact]
    public async Task ExportBelege_LoggtExport()
    {
        SetupBasicMocks();
        _receptaDataService.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<Kuestencode.Shared.Contracts.Recepta.ReceptaPaymentDto>());
        _fakturaClient.Setup(c => c.GetEuerPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<Kuestencode.Shared.Contracts.Faktura.InvoiceEuerPaymentDto>());

        var service = CreateService();
        await service.ExportBelegeAsync(Von, Bis, TestUserId);

        _exportLogRepo.Verify(r => r.AddAsync(It.Is<ExportLog>(log =>
            log.ExportTyp == ExportTyp.DatevBelege &&
            log.ZeitraumVon == Von &&
            log.ZeitraumBis == Bis &&
            log.ExportedByUserId == TestUserId
        )), Times.Once);
    }
}
