using Kuestencode.Werkbank.Saldo.Domain.Entities;

namespace Kuestencode.Werkbank.Saldo.Data.Repositories;

public interface IPeriodCloseRepository
{
    Task<List<PeriodClose>> GetAllAsync();

    /// <summary>Gibt den zuletzt erfassten Abschluss für exakt diesen Zeitraum zurück, falls vorhanden.</summary>
    Task<PeriodClose?> GetForRangeAsync(DateOnly von, DateOnly bis);

    Task<PeriodClose> AddAsync(PeriodClose periodClose);
}
