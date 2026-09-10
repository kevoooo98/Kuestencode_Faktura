namespace Kuestencode.Werkbank.Saldo.Domain.Dtos;

/// <summary>
/// Vollständige EÜR-Zusammenfassung für einen Zeitraum.
/// </summary>
public class EuerSummaryDto
{
    public DateOnly Von { get; set; }
    public DateOnly Bis { get; set; }

    // Einnahmen (aus Faktura)
    public decimal EinnahmenNetto { get; set; }
    public decimal EinnahmenMwst { get; set; }
    public decimal EinnahmenBrutto { get; set; }

    // Ausgaben (aus Recepta)
    public decimal AusgabenNetto { get; set; }
    public decimal AusgabenMwst { get; set; }
    public decimal AusgabenBrutto { get; set; }

    /// <summary>
    /// EÜR-Überschuss = Einnahmen Netto - Ausgaben Netto.
    /// </summary>
    public decimal Ueberschuss => EinnahmenNetto - AusgabenNetto;

    public List<EuerPositionDto> Einnahmen { get; set; } = new();
    public List<EuerPositionDto> Ausgaben { get; set; } = new();

    public bool IstKleinunternehmer { get; set; }
}


/// <summary>
/// Eine aggregierte Position in der EÜR (pro Konto).
/// </summary>
public class EuerPositionDto
{
    public string KontoNummer { get; set; } = string.Empty;
    public string KontoBezeichnung { get; set; } = string.Empty;
    public string Gruppe { get; set; } = string.Empty;
    public decimal BetragNetto { get; set; }
    public decimal MwstBetrag { get; set; }
    public decimal BetragBrutto { get; set; }
    public int AnzahlBelege { get; set; }
}

/// <summary>
/// Filter für EÜR-Abfragen.
/// </summary>
public class EuerFilterDto
{
    public DateOnly Von { get; set; }
    public DateOnly Bis { get; set; }
}

/// <summary>
/// DTO für Konten-Anzeige.
/// </summary>
public class KontoDto
{
    public Guid Id { get; set; }
    public string Kontenrahmen { get; set; } = string.Empty;
    public string KontoNummer { get; set; } = string.Empty;
    public string KontoBezeichnung { get; set; } = string.Empty;
    public string KontoTyp { get; set; } = string.Empty;
    public decimal? UstSatz { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// DTO für Kategorie-Konto-Mapping.
/// </summary>
public class KategorieKontoMappingDto
{
    public Guid Id { get; set; }
    public string Kontenrahmen { get; set; } = string.Empty;
    public string ReceiptaKategorie { get; set; } = string.Empty;
    public string KontoNummer { get; set; } = string.Empty;
    public string KontoBezeichnung { get; set; } = string.Empty;
    public bool IsCustom { get; set; }
}

/// <summary>
/// Update-Dto für ein Kategorie-Mapping.
/// </summary>
public class UpdateKategorieKontoMappingDto
{
    public string KontoNummer { get; set; } = string.Empty;
}

/// <summary>
/// DTO zum Anlegen eines neuen Kontos.
/// </summary>
public class CreateKontoDto
{
    public string Kontenrahmen { get; set; } = string.Empty;
    public string KontoNummer { get; set; } = string.Empty;
    public string KontoBezeichnung { get; set; } = string.Empty;
    public string KontoTyp { get; set; } = string.Empty;
    public decimal? UstSatz { get; set; }
}

/// <summary>
/// DTO zum Bearbeiten eines bestehenden Kontos.
/// Kontenrahmen/KontoNummer sind der fachliche Schlüssel und nicht änderbar.
/// </summary>
public class UpdateKontoDto
{
    public string KontoBezeichnung { get; set; } = string.Empty;
    public string KontoTyp { get; set; } = string.Empty;
    public decimal? UstSatz { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// DTO für Saldo-Einstellungen.
/// </summary>
public class SaldoSettingsDto
{
    public Guid Id { get; set; }
    public string Kontenrahmen { get; set; } = "SKR03";
    public string? BeraterNummer { get; set; }
    public string? MandantenNummer { get; set; }
    public int WirtschaftsjahrBeginn { get; set; } = 1;
}

/// <summary>
/// Update-Dto für Saldo-Einstellungen.
/// </summary>
public class UpdateSaldoSettingsDto
{
    public string Kontenrahmen { get; set; } = "SKR03";
    public string? BeraterNummer { get; set; }
    public string? MandantenNummer { get; set; }
    public int WirtschaftsjahrBeginn { get; set; } = 1;
}

/// <summary>
/// DTO für ein benutzerdefiniertes Konto-Mapping-Override.
/// </summary>
public class KontoMappingOverrideDto
{
    public Guid Id { get; set; }
    public string Kontenrahmen { get; set; } = string.Empty;
    public string Kategorie { get; set; } = string.Empty;
    public string KontoNummer { get; set; } = string.Empty;
    public string KontoBezeichnung { get; set; } = string.Empty;
    public DateOnly GueltigAb { get; set; }
    public DateOnly? GueltigBis { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Request zum Setzen oder Aktualisieren eines Mapping-Overrides.
/// </summary>
public class SetKontoMappingOverrideDto
{
    public string KontoNummer { get; set; } = string.Empty;
}

/// <summary>
/// Aufgelöstes Mapping für eine Kategorie (Override hat Vorrang vor Standard).
/// </summary>
public class ResolvedKontoMappingDto
{
    public string Kategorie { get; set; } = string.Empty;
    public string KontoNummer { get; set; } = string.Empty;
    public string KontoBezeichnung { get; set; } = string.Empty;
    /// <summary>True wenn ein benutzerdefinierter Override aktiv ist.</summary>
    public bool IsOverride { get; set; }
}

/// <summary>
/// Protokolleintrag für einen Export.
/// </summary>
public class ExportLogDto
{
    public Guid Id { get; set; }
    public string ExportTyp { get; set; } = string.Empty;
    public DateOnly ZeitraumVon { get; set; }
    public DateOnly ZeitraumBis { get; set; }
    public int AnzahlBuchungen { get; set; }
    public string DateiName { get; set; } = string.Empty;
    public long DateiGroesse { get; set; }
    public DateTime ExportedAt { get; set; }
    public Guid ExportedByUserId { get; set; }
}

/// <summary>
/// Informativer Abschluss eines Zeitraums nach einem Export.
/// </summary>
public class PeriodCloseDto
{
    public Guid Id { get; set; }
    public DateOnly ZeitraumVon { get; set; }
    public DateOnly ZeitraumBis { get; set; }
    public DateTime ClosedAt { get; set; }
    public Guid ClosedByUserId { get; set; }
    public Guid? ExportLogId { get; set; }
}
