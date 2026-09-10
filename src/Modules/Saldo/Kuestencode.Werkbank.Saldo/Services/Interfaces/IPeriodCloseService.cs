using Kuestencode.Werkbank.Saldo.Domain.Dtos;

namespace Kuestencode.Werkbank.Saldo.Services;

/// <summary>
/// Informativer Zeitraum-Abschluss nach einem Export. Rein deklarativ:
/// blockiert keine Bearbeitung in Faktura/Recepta, dient nur als Hinweis.
/// </summary>
public interface IPeriodCloseService
{
    /// <summary>Schließt den angegebenen Zeitraum ab (explizite Nutzeraktion, kein Automatismus).</summary>
    Task<PeriodCloseDto> ClosePeriodAsync(DateOnly von, DateOnly bis, Guid closedByUserId, Guid? exportLogId);

    /// <summary>Gibt den zuletzt erfassten Abschluss für exakt diesen Zeitraum zurück, falls vorhanden.</summary>
    Task<PeriodCloseDto?> GetForRangeAsync(DateOnly von, DateOnly bis);

    Task<List<PeriodCloseDto>> GetAllAsync();
}
