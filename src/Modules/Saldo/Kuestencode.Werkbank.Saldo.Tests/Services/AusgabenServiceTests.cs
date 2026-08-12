using FluentAssertions;
using Kuestencode.Shared.Contracts.Recepta;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Dtos;
using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Kuestencode.Werkbank.Saldo.Domain.Enums;
using Kuestencode.Werkbank.Saldo.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Services;

public class AusgabenServiceTests
{
    private readonly Mock<IReceptaDataService> _receptaData = new();
    private readonly Mock<IKategorieKontoMappingRepository> _mappingRepo = new();
    private readonly Mock<IKontoRepository> _kontoRepo = new();
    private readonly Mock<ISaldoSettingsRepository> _settingsRepo = new();

    private static readonly DateOnly Von = new(2026, 1, 1);
    private static readonly DateOnly Bis = new(2026, 12, 31);

    private AusgabenService CreateService() =>
        new(_receptaData.Object, _mappingRepo.Object, _kontoRepo.Object,
            _settingsRepo.Object, NullLogger<AusgabenService>.Instance);

    private void SetupKontenrahmen(string kontenrahmen = "SKR03")
    {
        _settingsRepo.Setup(r => r.GetAsync())
            .ReturnsAsync(new SaldoSettings { Kontenrahmen = kontenrahmen });
    }

    // Vollständig bezahlter Beleg (PaymentAmount == AmountGross => Ratio 1).
    private static ReceptaPaymentDto CreatePayment(
        string category, DateOnly invoiceDate, DateOnly paymentDate,
        decimal net, decimal taxRate, decimal tax, string? supplierName = null, string? docNumber = null,
        Guid? documentId = null)
    {
        var gross = net + tax;
        return new ReceptaPaymentDto
        {
            PaymentId = Guid.NewGuid(),
            DocumentId = documentId ?? Guid.NewGuid(),
            DocumentNumber = docNumber ?? "BE-001",
            SupplierName = supplierName ?? "Lieferant GmbH",
            InvoiceDate = invoiceDate,
            PaymentDate = paymentDate,
            PaymentAmount = gross,
            AmountNet = net,
            AmountTax = tax,
            AmountGross = gross,
            TaxRate = taxRate,
            Category = category
        };
    }

    // ─── GetAusgabenAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAusgaben_GibtBuchungenMitTypAusgabeZurueck()
    {
        SetupKontenrahmen();
        _mappingRepo.Setup(r => r.GetAllAsync("SKR03")).ReturnsAsync(new List<KategorieKontoMapping>());
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03")).ReturnsAsync(new List<Konto>());
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("Büromaterial", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5), 100m, 19m, 19m)
            });

        var service = CreateService();
        var result = await service.GetAusgabenAsync(Von, Bis);

        result.Should().HaveCount(1);
        result[0].Typ.Should().Be(BuchungsTyp.Ausgabe);
        result[0].Quelle.Should().Be("Recepta");
    }

    [Fact]
    public async Task GetAusgaben_SortiertNachZahlungsDatum()
    {
        SetupKontenrahmen();
        _mappingRepo.Setup(r => r.GetAllAsync("SKR03")).ReturnsAsync(new List<KategorieKontoMapping>());
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03")).ReturnsAsync(new List<Konto>());
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("Reise", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 10), 200m, 19m, 38m),
                CreatePayment("Büromaterial", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5), 100m, 19m, 19m)
            });

        var service = CreateService();
        var result = await service.GetAusgabenAsync(Von, Bis);

        result[0].ZahlungsDatum.Should().Be(new DateOnly(2026, 1, 5));
        result[1].ZahlungsDatum.Should().Be(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public async Task GetAusgaben_NutztKontoAusMappingWennVorhanden()
    {
        SetupKontenrahmen();
        _mappingRepo.Setup(r => r.GetAllAsync("SKR03"))
            .ReturnsAsync(new List<KategorieKontoMapping>
            {
                new() { ReceiptaKategorie = "Büromaterial", KontoNummer = "4930", Kontenrahmen = "SKR03" }
            });
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03"))
            .ReturnsAsync(new List<Konto>
            {
                new() { KontoNummer = "4930", KontoBezeichnung = "Bürobedarf", KontoTyp = KontoTyp.Ausgabe }
            });
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("Büromaterial", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5), 100m, 19m, 19m)
            });

        var service = CreateService();
        var result = await service.GetAusgabenAsync(Von, Bis);

        result[0].KontoNummer.Should().Be("4930");
        result[0].KontoBezeichnung.Should().Be("Bürobedarf");
    }

    [Fact]
    public async Task GetAusgaben_NutztFallback4980WennKeinMapping_SKR03()
    {
        SetupKontenrahmen("SKR03");
        _mappingRepo.Setup(r => r.GetAllAsync("SKR03")).ReturnsAsync(new List<KategorieKontoMapping>());
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03")).ReturnsAsync(new List<Konto>());
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("UnbekanntKategorie", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), 100m, 0m, 0m)
            });

        var service = CreateService();
        var result = await service.GetAusgabenAsync(Von, Bis);

        result[0].KontoNummer.Should().Be("4980");
    }

    [Fact]
    public async Task GetAusgaben_NutztFallback4900WennKeinMapping_SKR04()
    {
        SetupKontenrahmen("SKR04");
        _mappingRepo.Setup(r => r.GetAllAsync("SKR04")).ReturnsAsync(new List<KategorieKontoMapping>());
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR04")).ReturnsAsync(new List<Konto>());
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("UnbekanntKategorie", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), 100m, 0m, 0m)
            });

        var service = CreateService();
        var result = await service.GetAusgabenAsync(Von, Bis);

        result[0].KontoNummer.Should().Be("4900");
    }

    [Fact]
    public async Task GetAusgaben_TeilzahlungErhaeltFortlaufendenSuffix()
    {
        SetupKontenrahmen();
        _mappingRepo.Setup(r => r.GetAllAsync("SKR03")).ReturnsAsync(new List<KategorieKontoMapping>());
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03")).ReturnsAsync(new List<Konto>());
        var documentId = Guid.NewGuid();
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("Büromaterial", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 10), 50m, 19m, 9.5m, docNumber: "BE-001", documentId: documentId),
                CreatePayment("Büromaterial", new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 10), 50m, 19m, 9.5m, docNumber: "BE-001", documentId: documentId)
            });

        var service = CreateService();
        var result = await service.GetAusgabenAsync(Von, Bis);

        result.Should().HaveCount(2);
        result[0].QuelleId.Should().Be("BE-001/1");
        result[1].QuelleId.Should().Be("BE-001/2");
    }

    [Fact]
    public async Task GetAusgaben_VerwendetLieferantAlsBeschreibung()
    {
        SetupKontenrahmen();
        _mappingRepo.Setup(r => r.GetAllAsync("SKR03")).ReturnsAsync(new List<KategorieKontoMapping>());
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03")).ReturnsAsync(new List<Konto>());
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("Reise", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1),
                    200m, 19m, 38m, supplierName: "Deutsche Bahn AG", docNumber: "RK-2026-001")
            });

        var service = CreateService();
        var result = await service.GetAusgabenAsync(Von, Bis);

        result[0].Beschreibung.Should().Be("Deutsche Bahn AG");
        result[0].QuelleId.Should().Be("RK-2026-001");
        result[0].Kategorie.Should().Be("Reise");
    }

    [Fact]
    public async Task GetAusgaben_NutztKontenrahmenFallback_SKR03()
    {
        _settingsRepo.Setup(r => r.GetAsync()).ReturnsAsync((SaldoSettings?)null);
        _mappingRepo.Setup(r => r.GetAllAsync("SKR03")).ReturnsAsync(new List<KategorieKontoMapping>());
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03")).ReturnsAsync(new List<Konto>());
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis)).ReturnsAsync(new List<ReceptaPaymentDto>());

        var service = CreateService();
        await service.GetAusgabenAsync(Von, Bis);

        _mappingRepo.Verify(r => r.GetAllAsync("SKR03"), Times.Once);
    }

    [Fact]
    public async Task GetAusgaben_ZuflussAbflussprinzip_RechnungDezBezahltJan()
    {
        // Beleg aus Dez 2025, bezahlt Jan 2026 → muss in 2026 erscheinen
        var von2026 = new DateOnly(2026, 1, 1);
        var bis2026 = new DateOnly(2026, 12, 31);

        SetupKontenrahmen();
        _mappingRepo.Setup(r => r.GetAllAsync("SKR03")).ReturnsAsync(new List<KategorieKontoMapping>());
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03")).ReturnsAsync(new List<Konto>());
        _receptaData.Setup(s => s.GetPaymentsAsync(von2026, bis2026))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("Büromaterial", new DateOnly(2025, 12, 20), new DateOnly(2026, 1, 10), 100m, 19m, 19m)
            });

        var service = CreateService();
        var result = await service.GetAusgabenAsync(von2026, bis2026);

        result.Should().HaveCount(1);
        result[0].BelegDatum.Should().Be(new DateOnly(2025, 12, 20));
        result[0].ZahlungsDatum.Should().Be(new DateOnly(2026, 1, 10));
    }

    [Fact]
    public async Task GetAusgaben_MappingVorhandenAberKontoNichtInListe_NutztMappingNummer()
    {
        // Mapping zeigt auf Konto, das nicht im Konto-Repository ist → KontoNummer aus Mapping, Bezeichnung = Kategorie
        SetupKontenrahmen();
        _mappingRepo.Setup(r => r.GetAllAsync("SKR03"))
            .ReturnsAsync(new List<KategorieKontoMapping>
            {
                new() { ReceiptaKategorie = "Software", KontoNummer = "4970", Kontenrahmen = "SKR03" }
            });
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03")).ReturnsAsync(new List<Konto>());
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("Software", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), 50m, 19m, 9.5m)
            });

        var service = CreateService();
        var result = await service.GetAusgabenAsync(Von, Bis);

        result[0].KontoNummer.Should().Be("4970");
        result[0].KontoBezeichnung.Should().Be("Software");
    }

    [Fact]
    public async Task GetAusgaben_BelegDatumIstInvoiceDate_UnabhaengigVomZahlungsdatum()
    {
        SetupKontenrahmen();
        _mappingRepo.Setup(r => r.GetAllAsync("SKR03")).ReturnsAsync(new List<KategorieKontoMapping>());
        _kontoRepo.Setup(r => r.GetByKontenrahmenAsync("SKR03")).ReturnsAsync(new List<Konto>());
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("Reise", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 25), 200m, 19m, 38m)
            });

        var service = CreateService();
        var result = await service.GetAusgabenAsync(Von, Bis);

        result[0].BelegDatum.Should().Be(new DateOnly(2026, 3, 1));
        result[0].ZahlungsDatum.Should().Be(new DateOnly(2026, 3, 25));
    }

    // ─── GetSummeAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSumme_SummiertAmountNet()
    {
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("Büro", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), 300m, 19m, 57m),
                CreatePayment("Reise", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), 150m, 19m, 28.5m)
            });

        var service = CreateService();
        var result = await service.GetSummeAsync(Von, Bis);

        result.Should().Be(450m);
    }

    [Fact]
    public async Task GetSumme_GibtNullBeiLeeremErgebnis()
    {
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis)).ReturnsAsync(new List<ReceptaPaymentDto>());

        var service = CreateService();
        var result = await service.GetSummeAsync(Von, Bis);

        result.Should().Be(0m);
    }

    // ─── GetNachKategorieAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetNachKategorie_GruppiertKorrektNachKategorie()
    {
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis))
            .ReturnsAsync(new List<ReceptaPaymentDto>
            {
                CreatePayment("Büromaterial", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), 100m, 19m, 19m),
                CreatePayment("Büromaterial", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), 200m, 19m, 38m),
                CreatePayment("Reise",        new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), 500m, 19m, 95m)
            });

        var service = CreateService();
        var result = await service.GetNachKategorieAsync(Von, Bis);

        result.Should().ContainKey("Büromaterial").WhoseValue.Should().Be(300m);
        result.Should().ContainKey("Reise").WhoseValue.Should().Be(500m);
    }

    [Fact]
    public async Task GetNachKategorie_SummeGleichGetSumme()
    {
        // Aggregationskonsistenz: Summe aller Kategorien muss GetSummeAsync entsprechen
        var payments = new List<ReceptaPaymentDto>
        {
            CreatePayment("Büromaterial", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), 100m, 19m, 19m),
            CreatePayment("Reise",        new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), 250m, 19m, 47.5m),
            CreatePayment("Software",     new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1),  80m, 19m, 15.2m)
        };
        _receptaData.Setup(s => s.GetPaymentsAsync(Von, Bis)).ReturnsAsync(payments);

        var service = CreateService();
        var summe = await service.GetSummeAsync(Von, Bis);
        var nachKategorie = await service.GetNachKategorieAsync(Von, Bis);

        nachKategorie.Values.Sum().Should().Be(summe);
    }
}
