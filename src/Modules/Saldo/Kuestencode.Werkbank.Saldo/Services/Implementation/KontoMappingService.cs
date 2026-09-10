using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Dtos;
using Kuestencode.Werkbank.Saldo.Domain.Enums;

namespace Kuestencode.Werkbank.Saldo.Services;

/// <summary>
/// Löst Konto-Nummern für Einnahmen und Ausgaben auf.
/// Lookup-Reihenfolge: Override (DB) → Standard-Mapping (DB) → Fallback (hardcoded).
/// </summary>
public class KontoMappingService : IKontoMappingService
{
    private readonly IKontoMappingOverrideRepository _overrideRepo;
    private readonly IKategorieKontoMappingRepository _mappingRepo;
    private readonly IKontoRepository _kontoRepo;
    private readonly ISaldoSettingsRepository _settingsRepo;
    private readonly ILogger<KontoMappingService> _logger;

    public KontoMappingService(
        IKontoMappingOverrideRepository overrideRepo,
        IKategorieKontoMappingRepository mappingRepo,
        IKontoRepository kontoRepo,
        ISaldoSettingsRepository settingsRepo,
        ILogger<KontoMappingService> logger)
    {
        _overrideRepo = overrideRepo;
        _mappingRepo = mappingRepo;
        _kontoRepo = kontoRepo;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    public async Task<string> GetEinnahmenKontoAsync(decimal ustSatz)
    {
        var kontenrahmen = await GetKontenrahmenAsync();
        var konten = await _kontoRepo.GetByKontenrahmenAsync(kontenrahmen);

        var konto = konten.FirstOrDefault(k =>
            k.KontoTyp == KontoTyp.Einnahme &&
            k.UstSatz.HasValue &&
            k.UstSatz.Value == ustSatz);

        if (konto != null) return konto.KontoNummer;

        // Fallback: SKR03/SKR04 Defaults
        _logger.LogWarning("Kein Einnahmen-Konto für USt {UstSatz}% in {Kontenrahmen} gefunden, nutze Fallback", ustSatz, kontenrahmen);
        return kontenrahmen == "SKR04"
            ? ustSatz switch { 19 => "4400", 7 => "4300", _ => "4120" }
            : ustSatz switch { 19 => "8400", 7 => "8300", _ => "8120" };
    }

    public async Task<string> GetAusgabenKontoAsync(string kategorie, DateOnly? asOfDate = null)
    {
        var kontenrahmen = await GetKontenrahmenAsync();

        // 1. Override prüfen (hat Vorrang)
        var overr = await _overrideRepo.GetByKategorieAsync(kontenrahmen, kategorie, asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
        if (overr != null) return overr.KontoNummer;

        // 2. Standard-Mapping
        var mapping = await _mappingRepo.GetByKategorieAsync(kontenrahmen, kategorie);
        if (mapping != null) return mapping.KontoNummer;

        // 3. Fallback
        _logger.LogWarning("Kein Mapping für Kategorie {Kategorie} in {Kontenrahmen} gefunden, nutze Fallback", kategorie, kontenrahmen);
        return kontenrahmen == "SKR04" ? "6300" : "4900";
    }

    public async Task<string> GetBankKontoAsync()
    {
        var kontenrahmen = await GetKontenrahmenAsync();
        var konten = await _kontoRepo.GetByKontenrahmenAsync(kontenrahmen);

        var konto = konten.FirstOrDefault(k => k.KontoTyp == KontoTyp.Bank);
        if (konto != null) return konto.KontoNummer;

        return kontenrahmen == "SKR04" ? "1800" : "1200";
    }

    public async Task<List<KontoDto>> GetAlleKontenAsync(string kontenrahmen)
    {
        var konten = await _kontoRepo.GetByKontenrahmenAsync(kontenrahmen);
        return konten.Select(k => new KontoDto
        {
            Id = k.Id,
            Kontenrahmen = k.Kontenrahmen,
            KontoNummer = k.KontoNummer,
            KontoBezeichnung = k.KontoBezeichnung,
            KontoTyp = k.KontoTyp.ToString(),
            UstSatz = k.UstSatz,
            IsActive = k.IsActive
        }).ToList();
    }

    public async Task<KontoMappingOverrideDto> UpdateMappingAsync(string kategorie, string kontoNummer)
    {
        var kontenrahmen = await GetKontenrahmenAsync();

        // Ziel-Konto muss im Kontenrahmen existieren
        if (!await _kontoRepo.ExistsAsync(kontenrahmen, kontoNummer))
            throw new InvalidOperationException($"Konto {kontoNummer} nicht im Kontenrahmen {kontenrahmen} gefunden.");

        var heute = DateOnly.FromDateTime(DateTime.UtcNow);
        var overr = await _overrideRepo.SetOverrideAsync(kontenrahmen, kategorie, kontoNummer, heute);
        return MapToDto(overr);
    }

    public async Task ResetMappingAsync(string kategorie)
    {
        var kontenrahmen = await GetKontenrahmenAsync();
        await _overrideRepo.DeleteAsync(kontenrahmen, kategorie, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public async Task<List<KontoMappingOverrideDto>> GetOverridesAsync()
    {
        var kontenrahmen = await GetKontenrahmenAsync();
        var overrides = await _overrideRepo.GetAllAsync(kontenrahmen);
        return overrides.Select(MapToDto).ToList();
    }

    public async Task<List<ResolvedKontoMappingDto>> GetResolvedMappingsAsync(string kontenrahmen)
    {
        var mappings = await _mappingRepo.GetAllAsync(kontenrahmen);
        var overrides = await _overrideRepo.GetAllAsync(kontenrahmen);
        var overrideDict = overrides.ToDictionary(o => o.Kategorie, o => o);
        var konten = await _kontoRepo.GetByKontenrahmenAsync(kontenrahmen);
        var kontoDict = konten.ToDictionary(k => k.KontoNummer, k => k.KontoBezeichnung);

        return mappings.Select(m =>
        {
            if (overrideDict.TryGetValue(m.ReceiptaKategorie, out var overr))
            {
                return new ResolvedKontoMappingDto
                {
                    Kategorie = m.ReceiptaKategorie,
                    KontoNummer = overr.KontoNummer,
                    KontoBezeichnung = kontoDict.GetValueOrDefault(overr.KontoNummer, overr.KontoNummer),
                    IsOverride = true
                };
            }

            return new ResolvedKontoMappingDto
            {
                Kategorie = m.ReceiptaKategorie,
                KontoNummer = m.KontoNummer,
                KontoBezeichnung = m.Konto?.KontoBezeichnung
                    ?? kontoDict.GetValueOrDefault(m.KontoNummer, m.KontoNummer),
                IsOverride = false
            };
        }).ToList();
    }

    private async Task<string> GetKontenrahmenAsync()
    {
        var settings = await _settingsRepo.GetAsync();
        return settings?.Kontenrahmen ?? "SKR03";
    }

    private static KontoMappingOverrideDto MapToDto(Domain.Entities.KontoMappingOverride o) => new()
    {
        Id = o.Id,
        Kontenrahmen = o.Kontenrahmen,
        Kategorie = o.Kategorie,
        KontoNummer = o.KontoNummer,
        KontoBezeichnung = string.Empty,
        GueltigAb = o.GueltigAb,
        GueltigBis = o.GueltigBis,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt
    };
}
