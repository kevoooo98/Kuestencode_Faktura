using Kuestencode.Werkbank.Saldo.Domain.Entities;

namespace Kuestencode.Werkbank.Saldo.Data.Repositories;

/// <summary>
/// Repository für benutzerdefinierte Konto-Mapping-Overrides.
/// </summary>
public interface IKontoMappingOverrideRepository
{
    /// <summary>Gibt alle aktuell offenen (aktiven) Overrides eines Kontenrahmens zurück.</summary>
    Task<List<KontoMappingOverride>> GetAllAsync(string kontenrahmen);

    /// <summary>Gibt den zum Stichtag gültigen Override zurück, falls vorhanden.</summary>
    Task<KontoMappingOverride?> GetByKategorieAsync(string kontenrahmen, string kategorie, DateOnly asOfDate);

    /// <summary>
    /// Schließt einen offenen Override für die Kategorie (falls vorhanden) und legt einen
    /// neuen ab <paramref name="gueltigAb"/> an. Bereits vergangene Zeiträume bleiben unverändert.
    /// </summary>
    Task<KontoMappingOverride> SetOverrideAsync(string kontenrahmen, string kategorie, string kontoNummer, DateOnly gueltigAb);

    /// <summary>Schließt den offenen Override ab dem Stichtag (Standard-Mapping gilt danach wieder).</summary>
    Task DeleteAsync(string kontenrahmen, string kategorie, DateOnly asOfDate);
}
