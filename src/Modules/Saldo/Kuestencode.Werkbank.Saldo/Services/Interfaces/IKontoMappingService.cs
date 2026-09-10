using Kuestencode.Werkbank.Saldo.Domain.Dtos;

namespace Kuestencode.Werkbank.Saldo.Services;

/// <summary>
/// Löst Konto-Nummern für Einnahmen und Ausgaben auf.
/// Overrides (benutzerdefiniert) haben Vorrang vor Standard-Mappings.
/// </summary>
public interface IKontoMappingService
{
    /// <summary>
    /// Gibt die Kontonummer für eine Einnahme anhand des USt-Satzes zurück.
    /// </summary>
    Task<string> GetEinnahmenKontoAsync(decimal ustSatz);

    /// <summary>
    /// Gibt die Kontonummer für eine Ausgabe anhand der Recepta-Kategorie zurück.
    /// Benutzerdefinierte Overrides haben Vorrang vor Standard-Mappings.
    /// Der Override wird zum <paramref name="asOfDate"/> aufgelöst (Standard: heute).
    /// </summary>
    Task<string> GetAusgabenKontoAsync(string kategorie, DateOnly? asOfDate = null);

    /// <summary>
    /// Gibt die Kontonummer des konfigurierten Bankkontos zurück.
    /// </summary>
    Task<string> GetBankKontoAsync();

    /// <summary>
    /// Gibt alle Konten des aktiven Kontenrahmens zurück.
    /// </summary>
    Task<List<KontoDto>> GetAlleKontenAsync(string kontenrahmen);

    /// <summary>
    /// Setzt oder aktualisiert einen benutzerdefinierten Override für eine Kategorie.
    /// </summary>
    Task<KontoMappingOverrideDto> UpdateMappingAsync(string kategorie, string kontoNummer);

    /// <summary>
    /// Entfernt einen benutzerdefinierten Override (stellt Standard-Mapping wieder her).
    /// </summary>
    Task ResetMappingAsync(string kategorie);

    /// <summary>
    /// Gibt alle aktiven Overrides für den konfigurierten Kontenrahmen zurück.
    /// </summary>
    Task<List<KontoMappingOverrideDto>> GetOverridesAsync();

    /// <summary>
    /// Gibt das aufgelöste Mapping für alle Kategorien zurück
    /// (Override wenn vorhanden, sonst Standard).
    /// </summary>
    Task<List<ResolvedKontoMappingDto>> GetResolvedMappingsAsync(string kontenrahmen);
}
