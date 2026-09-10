using System.IO.Compression;
using System.Text;
using Kuestencode.Core.Interfaces;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Faktura;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Dtos;
using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Kuestencode.Werkbank.Saldo.Domain.Enums;

namespace Kuestencode.Werkbank.Saldo.Services;

/// <summary>
/// Erstellt DATEV-konforme Exporte nach EXTF-Spezifikation.
///
/// DATEV-Encoding: Windows-1252 (nicht UTF-8!)
/// Dezimaltrennzeichen: Komma
/// Feldtrennzeichen: Semikolon
/// Textbegrenzer: Anführungszeichen
/// </summary>
public class DatevExportService : IDatevExportService
{
    static DatevExportService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // DATEV EXTF-Versionsnummern
    private const int DatevFormatKennzeichen = 700;
    private const int DatevVersionsnummer = 21;
    private const string DatevDatenkategorie = "Buchungsstapel";
    private const int DatevFormatversion = 7;

    private readonly ISaldoAggregationService _saldoService;
    private readonly ISaldoSettingsRepository _settingsRepo;
    private readonly IKontoMappingService _kontoMappingService;
    private readonly IFakturaApiClient _fakturaClient;
    private readonly IReceptaApiClient _receptaClient;
    private readonly IReceptaDataService _receptaDataService;
    private readonly IExportLogRepository _exportLogRepo;
    private readonly ICompanyService _companyService;
    private readonly ILogger<DatevExportService> _logger;

    public DatevExportService(
        ISaldoAggregationService saldoService,
        ISaldoSettingsRepository settingsRepo,
        IKontoMappingService kontoMappingService,
        IFakturaApiClient fakturaClient,
        IReceptaApiClient receptaClient,
        IReceptaDataService receptaDataService,
        IExportLogRepository exportLogRepo,
        ICompanyService companyService,
        ILogger<DatevExportService> logger)
    {
        _saldoService = saldoService;
        _settingsRepo = settingsRepo;
        _kontoMappingService = kontoMappingService;
        _fakturaClient = fakturaClient;
        _receptaClient = receptaClient;
        _receptaDataService = receptaDataService;
        _exportLogRepo = exportLogRepo;
        _companyService = companyService;
        _logger = logger;
    }

    // ─── BUCHUNGSSTAPEL ───────────────────────────────────────────────────────

    public async Task<byte[]> ExportBuchungsstapelAsync(DateOnly von, DateOnly bis, Guid exportedByUserId)
    {
        var settings = await _settingsRepo.GetAsync();
        var buchungen = await _saldoService.GetAlleBuchungenAsync(von, bis);
        var bankKonto = await _kontoMappingService.GetBankKontoAsync();

        string exportiertVon;
        try { exportiertVon = (await _companyService.GetCompanyAsync())?.DisplayName ?? string.Empty; }
        catch { exportiertVon = string.Empty; }

        var sb = new StringBuilder();

        // Zeile 1: DATEV-Header
        sb.AppendLine(GenerateDatevHeader(settings, von, bis, buchungen.Count, exportiertVon));

        // Zeile 2: Spaltenüberschriften
        sb.AppendLine("\"Umsatz\";\"Soll/Haben-Kennzeichen\";\"WKZ Umsatz\";\"Konto\";\"Gegenkonto\";\"BU-Schlüssel\";\"Belegdatum\";\"Belegfeld 1\";\"Buchungstext\"");

        // Zeile 3+: Buchungen
        foreach (var buchung in buchungen)
        {
            sb.AppendLine(GenerateBuchungszeile(buchung, bankKonto));
        }

        var csv = sb.ToString();
        _logger.LogInformation("DATEV-Buchungsstapel: {Count} Buchungen, {Von} - {Bis}", buchungen.Count, von, bis);

        await LogExportAsync(ExportTyp.DatevBuchungsstapel, von, bis, buchungen.Count,
            $"DATEV_Buchungsstapel_{von:yyyyMMdd}_{bis:yyyyMMdd}.csv",
            Encoding.GetEncoding(1252).GetByteCount(csv), exportedByUserId);

        // DATEV erwartet Windows-1252
        return Encoding.GetEncoding(1252).GetBytes(csv);
    }

    private static string GenerateDatevHeader(SaldoSettings? settings, DateOnly von, DateOnly bis, int anzahlBuchungen, string exportiertVon)
    {
        // DATEV EXTF-Header (Zeile 1, 31 Felder)
        // Spezifikation: DATEV-Schnittstelle EXTF Buchungsstapel
        var erstelltAm = DateTime.Now.ToString("yyyyMMddHHmmss");
        var beraterNr = settings?.BeraterNummer ?? string.Empty;
        var mandantenNr = settings?.MandantenNummer ?? string.Empty;
        var wjBeginn = settings?.WirtschaftsjahrBeginn ?? 1;
        var wjBeginnStr = $"{von.Year}{wjBeginn:D2}01"; // z.B. 20260101

        // Sachkontennummernlänge: 4 Stellen (SKR03/SKR04)
        const int sachkontoLaenge = 4;

        return "\"EXTF\"" +
               $";{DatevFormatKennzeichen}" +
               $";{DatevVersionsnummer}" +
               $";\"{DatevDatenkategorie}\"" +
               $";{DatevFormatversion}" +
               $";\"{erstelltAm}\"" +
               ";;" +                                   // Importiert, Herkunft (leer)
               $"\"{exportiertVon}\"" +                 // Exportiert von (Firmenname)
               ";;" +                                   // Leer
               $";\"{beraterNr}\"" +
               $";\"{mandantenNr}\"" +
               $";\"{wjBeginnStr}\"" +
               $";{sachkontoLaenge}" +
               $";\"{von:yyyyMMdd}\"" +                 // Buchungsdatum von
               $";\"{bis:yyyyMMdd}\"" +                 // Buchungsdatum bis
               ";\"\"" +                                // Bezeichnung (leer)
               ";\"\"" +                                // Diktatkürzel (leer)
               ";1" +                                   // Buchungstyp: 1=Fibu
               ";0" +                                   // Rechnungslegungszweck
               ";0" +                                   // Festschreibung: 0=nein
               $";\"{GetWaehrungskennzeichen()}\"";     // Währungskennzeichen
    }

    private static string GetWaehrungskennzeichen() => "EUR";

    private static string GenerateBuchungszeile(BuchungDto buchung, string bankKonto)
    {
        // Soll/Haben: Einnahme = Zugang auf Bankkonto (Soll), Ausgabe = Abgang (Haben).
        // Ein negativer Brutto-Betrag (z.B. Gutschrift) kehrt die Buchungsrichtung um -
        // das Umsatzfeld selbst darf laut DATEV EXTF-Format nie ein Vorzeichen tragen.
        var isNegative = buchung.Brutto < 0;
        var isEinnahme = buchung.Typ == BuchungsTyp.Einnahme;
        var sollHaben = isEinnahme != isNegative ? "S" : "H";

        // BU-Schlüssel (Steuerkennzeichen)
        var buSchluessel = GetBuSchluessel(buchung.Typ, buchung.UstSatz);

        // Belegdatum: ddMMyyyy → DATEV erwartet TTMMJJJJ (8-stellig ohne Trennzeichen)
        var belegDatum = buchung.ZahlungsDatum.ToString("ddMMyyyy");

        // Betrag: Brutto, Komma als Dezimaltrennzeichen (DATEV-Standard), immer ohne Vorzeichen
        var betrag = Math.Abs(buchung.Brutto).ToString("0.00").Replace('.', ',');

        // Buchungstext: max. 60 Zeichen, Sonderzeichen bereinigen
        var buchungstext = SanitizeText(buchung.Beschreibung, 60);

        // Belegfeld 1 (Rechnungsnummer): max. 36 Zeichen
        var belegfeld1 = SanitizeText(buchung.QuelleId, 36);

        // Konto/Gegenkonto: Bank ↔ Erlös/Aufwand-Konto
        // Einnahme: Konto = Bank, Gegenkonto = Erlöskonto
        // Ausgabe:  Konto = Bank, Gegenkonto = Aufwandskonto
        return $"{betrag};\"{sollHaben}\";\"EUR\";{bankKonto};{buchung.KontoNummer};{buSchluessel};\"{belegDatum}\";\"{belegfeld1}\";\"{buchungstext}\"";
    }

    /// <summary>
    /// BU-Schlüssel nach DATEV-Steuersystematik:
    /// Einnahmen: 3 = 19% USt, 2 = 7% USt, 0 = keine USt
    /// Ausgaben:  9 = 19% Vorsteuer, 8 = 7% Vorsteuer, 0 = keine VSt
    /// </summary>
    private static int GetBuSchluessel(BuchungsTyp typ, decimal ustSatz)
    {
        if (typ == BuchungsTyp.Einnahme)
        {
            return ustSatz switch
            {
                19 => 3,
                7  => 2,
                _  => 0
            };
        }
        else
        {
            return ustSatz switch
            {
                19 => 9,
                7  => 8,
                _  => 0
            };
        }
    }

    /// <summary>
    /// Bereinigt Text für DATEV: entfernt Semikolons, Anführungszeichen und kürzt auf maxLen.
    /// </summary>
    private static string SanitizeText(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sanitized = text
            .Replace('"', '\'')
            .Replace(';', ',')
            .Replace('\n', ' ')
            .Replace('\r', ' ');
        return sanitized.Length > maxLen ? sanitized[..maxLen] : sanitized;
    }

    // ─── BELEGE-ZIP ──────────────────────────────────────────────────────────

    public async Task<byte[]> ExportBelegeAsync(DateOnly von, DateOnly bis, Guid exportedByUserId)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var fileCount = 0;

            // Faktura-Rechnungen als PDF
            fileCount += await AddFakturaRechnungenAsync(archive, von, bis);

            // Recepta-Belege (hochgeladene Dateien)
            fileCount += await AddReceptaBelegeAsync(archive, von, bis);

            _logger.LogInformation("DATEV-Belege-ZIP: {Count} Dateien, {Von} - {Bis}", fileCount, von, bis);

            await LogExportAsync(ExportTyp.DatevBelege, von, bis, fileCount,
                $"DATEV_Belege_{von:yyyyMMdd}_{bis:yyyyMMdd}.zip",
                zipStream.Length, exportedByUserId);
        }

        return zipStream.ToArray();
    }

    private async Task<int> AddFakturaRechnungenAsync(ZipArchive archive, DateOnly von, DateOnly bis)
    {
        List<Kuestencode.Shared.Contracts.Faktura.InvoiceEuerPaymentDto> payments;
        try
        {
            payments = await _fakturaClient.GetEuerPaymentsAsync(von, bis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der Faktura-Zahlungen für Belege-Export");
            return 0;
        }

        // One PDF per unique invoice, regardless of how many partial payments exist
        var uniqueInvoices = payments
            .DistinctBy(p => p.InvoiceId)
            .ToList();

        var count = 0;
        foreach (var payment in uniqueInvoices)
        {
            try
            {
                var pdfBytes = await _fakturaClient.GenerateInvoicePdfAsync(payment.InvoiceId);
                var belegDatum = payment.PaymentDate;

                var fileName = $"{belegDatum:yyyy-MM-dd}_RE_{SanitizeFileName(payment.InvoiceNumber)}.pdf";
                AddFileToZip(archive, $"Rechnungen/{fileName}", pdfBytes);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PDF für Rechnung {InvoiceNumber} konnte nicht generiert werden", payment.InvoiceNumber);
            }
        }

        return count;
    }

    private async Task<int> AddReceptaBelegeAsync(ZipArchive archive, DateOnly von, DateOnly bis)
    {
        List<Kuestencode.Shared.Contracts.Recepta.ReceptaPaymentDto> payments;
        try
        {
            payments = await _receptaDataService.GetPaymentsAsync(von, bis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der Recepta-Zahlungen für Belege-Export");
            return 0;
        }

        // One file per unique document, regardless of how many partial payments exist
        var uniqueDocuments = payments
            .DistinctBy(p => p.DocumentId)
            .ToList();

        var count = 0;
        foreach (var payment in uniqueDocuments)
        {
            try
            {
                var files = await _receptaClient.GetFilesByDocumentAsync(payment.DocumentId);
                if (files.Count == 0) continue;

                var primaryFile = files[0];
                var downloaded = await _receptaClient.DownloadFileAsync(primaryFile.Id);
                if (downloaded == null) continue;

                var (data, _, _) = downloaded.Value;
                var ext = Path.GetExtension(primaryFile.FileName).TrimStart('.');
                if (string.IsNullOrEmpty(ext)) ext = "pdf";

                var fileName = $"{payment.PaymentDate:yyyy-MM-dd}_ER_{SanitizeFileName(payment.DocumentNumber)}.{ext}";
                AddFileToZip(archive, $"Belege/{fileName}", data);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Datei für Beleg {DocumentNumber} konnte nicht geladen werden", payment.DocumentNumber);
            }
        }

        return count;
    }

    private static void AddFileToZip(ZipArchive archive, string entryName, byte[] data)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(data, 0, data.Length);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    // ─── EXPORT-HISTORIE ─────────────────────────────────────────────────────

    public async Task<ExportLogDto?> GetLetztenExportAsync()
    {
        var alle = await _exportLogRepo.GetAllAsync();
        var letzter = alle.FirstOrDefault();
        return letzter == null ? null : MapToDto(letzter);
    }

    public async Task<List<ExportLogDto>> GetExportHistorieAsync()
    {
        var alle = await _exportLogRepo.GetAllAsync();
        return alle.Select(MapToDto).ToList();
    }

    private async Task LogExportAsync(ExportTyp typ, DateOnly von, DateOnly bis, int anzahl, string dateiName, long dateiGroesse, Guid exportedByUserId)
    {
        try
        {
            var log = new ExportLog
            {
                ExportTyp = typ,
                ZeitraumVon = von,
                ZeitraumBis = bis,
                AnzahlBuchungen = anzahl,
                DateiName = dateiName,
                DateiGroesse = dateiGroesse,
                ExportedAt = DateTime.UtcNow,
                ExportedByUserId = exportedByUserId
            };
            await _exportLogRepo.AddAsync(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Speichern des Export-Logs");
        }
    }

    private static ExportLogDto MapToDto(ExportLog log) => new()
    {
        Id = log.Id,
        ExportTyp = log.ExportTyp.ToString(),
        ZeitraumVon = log.ZeitraumVon,
        ZeitraumBis = log.ZeitraumBis,
        AnzahlBuchungen = log.AnzahlBuchungen,
        DateiName = log.DateiName,
        DateiGroesse = log.DateiGroesse,
        ExportedAt = log.ExportedAt,
        ExportedByUserId = log.ExportedByUserId
    };
}
