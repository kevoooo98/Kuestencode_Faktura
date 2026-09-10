using Kuestencode.Werkbank.Saldo.Domain.Dtos;

namespace Kuestencode.Werkbank.Saldo.Services;

/// <summary>
/// Erstellt DATEV-konforme Exporte (Buchungsstapel EXTF-Format, Belege-ZIP).
/// </summary>
public interface IDatevExportService
{
    /// <summary>
    /// Erzeugt einen DATEV-Buchungsstapel im EXTF-Format (Windows-1252, Semikolon-CSV).
    /// </summary>
    Task<byte[]> ExportBuchungsstapelAsync(DateOnly von, DateOnly bis, Guid exportedByUserId);

    /// <summary>
    /// Erzeugt ein ZIP-Archiv mit allen Belegen des Zeitraums als PDF.
    /// Faktura-Rechnungen werden on-the-fly generiert, Recepta-Belege sind die hochgeladenen Dateien.
    /// </summary>
    Task<byte[]> ExportBelegeAsync(DateOnly von, DateOnly bis, Guid exportedByUserId);

    /// <summary>
    /// Gibt den letzten Export zurück (beliebiger Typ).
    /// </summary>
    Task<ExportLogDto?> GetLetztenExportAsync();

    /// <summary>
    /// Gibt die gesamte Export-Historie zurück, neueste zuerst.
    /// </summary>
    Task<List<ExportLogDto>> GetExportHistorieAsync();
}
