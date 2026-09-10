using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Dtos;
using Kuestencode.Werkbank.Saldo.Domain.Enums;

namespace Kuestencode.Werkbank.Saldo.Services;

/// <summary>
/// Aggregiert Recepta-Zahlungen nach Zufluss-/Abflussprinzip (§11 EStG, PaymentDate).
/// Teilzahlungen erscheinen im Monat ihrer Zahlung; Beträge werden anteilig aufgeteilt.
/// </summary>
public class AusgabenService : IAusgabenService
{
    private readonly IReceptaDataService _receptaData;
    private readonly IKategorieKontoMappingRepository _mappingRepo;
    private readonly IKontoMappingOverrideRepository _overrideRepo;
    private readonly IKontoRepository _kontoRepo;
    private readonly ISaldoSettingsRepository _settingsRepo;
    private readonly ILogger<AusgabenService> _logger;

    public AusgabenService(
        IReceptaDataService receptaData,
        IKategorieKontoMappingRepository mappingRepo,
        IKontoMappingOverrideRepository overrideRepo,
        IKontoRepository kontoRepo,
        ISaldoSettingsRepository settingsRepo,
        ILogger<AusgabenService> logger)
    {
        _receptaData = receptaData;
        _mappingRepo = mappingRepo;
        _overrideRepo = overrideRepo;
        _kontoRepo = kontoRepo;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    public async Task<List<BuchungDto>> GetAusgabenAsync(DateOnly von, DateOnly bis)
    {
        var payments = await _receptaData.GetPaymentsAsync(von, bis);
        var settings = await _settingsRepo.GetAsync();
        var kontenrahmen = settings?.Kontenrahmen ?? "SKR03";
        var mappings = await _mappingRepo.GetAllAsync(kontenrahmen);
        var konten = await _kontoRepo.GetByKontenrahmenAsync(kontenrahmen);

        var fallbackKonto = kontenrahmen == "SKR04" ? "4900" : "4980";

        var buchungen = new List<BuchungDto>();

        // Group by DocumentId to detect partial payments (/1, /2 suffix)
        var byDocument = payments.GroupBy(p => p.DocumentId).ToList();

        foreach (var group in byDocument)
        {
            var isPartial = group.Count() > 1;
            var docPayments = group.OrderBy(p => p.PaymentDate).ToList();

            for (var n = 0; n < docPayments.Count; n++)
            {
                var payment = docPayments[n];
                var sign = payment.AmountGross >= 0 ? 1m : -1m;
                var ratio = payment.AmountGross != 0
                    ? Math.Abs(payment.PaymentAmount) / Math.Abs(payment.AmountGross)
                    : 1m;
                var quelleId = isPartial
                    ? $"{payment.DocumentNumber}/{n + 1}"
                    : payment.DocumentNumber;

                var overr = await _overrideRepo.GetByKategorieAsync(kontenrahmen, payment.Category, payment.PaymentDate);
                var mapping = mappings.FirstOrDefault(m => m.ReceiptaKategorie == payment.Category);
                var kontoNummer = overr?.KontoNummer ?? mapping?.KontoNummer;
                var konto = kontoNummer != null
                    ? konten.FirstOrDefault(k => k.KontoNummer == kontoNummer)
                    : null;

                buchungen.Add(new BuchungDto
                {
                    Id = payment.DocumentId,
                    Quelle = "Recepta",
                    QuelleId = quelleId,
                    BelegDatum = payment.InvoiceDate,
                    ZahlungsDatum = payment.PaymentDate,
                    Beschreibung = payment.SupplierName,
                    Netto = payment.AmountNet * ratio * sign,
                    Ust = payment.AmountTax * ratio * sign,
                    Brutto = payment.AmountGross * ratio * sign,
                    UstSatz = payment.TaxRate,
                    Kategorie = payment.Category,
                    KontoNummer = konto?.KontoNummer ?? kontoNummer ?? fallbackKonto,
                    KontoBezeichnung = konto?.KontoBezeichnung ?? payment.Category,
                    Typ = sign > 0 ? BuchungsTyp.Ausgabe : BuchungsTyp.Einnahme
                });
            }
        }

        return buchungen.OrderBy(b => b.ZahlungsDatum).ToList();
    }

    public async Task<decimal> GetSummeAsync(DateOnly von, DateOnly bis)
    {
        var payments = await _receptaData.GetPaymentsAsync(von, bis);
        return payments.Sum(p =>
        {
            var ratio = p.AmountGross != 0 ? p.PaymentAmount / p.AmountGross : 1m;
            return p.AmountNet * ratio;
        });
    }

    public async Task<Dictionary<string, decimal>> GetNachKategorieAsync(DateOnly von, DateOnly bis)
    {
        var payments = await _receptaData.GetPaymentsAsync(von, bis);
        return payments
            .GroupBy(p => p.Category)
            .ToDictionary(g => g.Key, g => g.Sum(p =>
            {
                var ratio = p.AmountGross != 0 ? p.PaymentAmount / p.AmountGross : 1m;
                return p.AmountNet * ratio;
            }));
    }
}
