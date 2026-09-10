using Kuestencode.Core.Auditing;
using Kuestencode.Werkbank.Recepta.Controllers.Dtos;
using Kuestencode.Werkbank.Recepta.Domain.Dtos;
using Kuestencode.Werkbank.Recepta.Domain.Enums;

namespace Kuestencode.Werkbank.Recepta.Services;

/// <summary>
/// Service für Belegverwaltung.
/// </summary>
public interface IDocumentService
{
    /// <summary>
    /// Lädt alle Belege mit optionalem Filter.
    /// </summary>
    Task<IEnumerable<DocumentDto>> GetAllAsync(DocumentFilterDto filter);

    /// <summary>
    /// Lädt einen Beleg anhand der ID.
    /// </summary>
    Task<DocumentDto?> GetByIdAsync(Guid id);

    /// <summary>
    /// Erstellt einen neuen Beleg.
    /// </summary>
    Task<DocumentDto> CreateAsync(CreateDocumentDto dto);

    /// <summary>
    /// Erstellt einen Beleg aus einem Scan (OCR + Pattern-Extraktion).
    /// </summary>
    Task<ScanResultDto> CreateFromScanAsync(Stream file, string fileName);

    /// <summary>
    /// Aktualisiert einen Beleg.
    /// </summary>
    Task<DocumentDto> UpdateAsync(Guid id, UpdateDocumentDto dto);

    /// <summary>
    /// Ändert den Status eines Belegs. Bei Übergang zu "Paid" kann ein Zahlungsdatum übergeben werden.
    /// </summary>
    Task ChangeStatusAsync(Guid id, DocumentStatus newStatus, DateOnly? paidDate = null);

    /// <summary>
    /// Triggert den Lerneffekt: Lernt Patterns aus den aktuellen Beleg-Daten.
    /// </summary>
    Task LearnPatternsAsync(Guid id);

    /// <summary>
    /// Löscht einen Beleg (nur bei Status Draft).
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// Generiert die nächste verfügbare Belegnummer.
    /// </summary>
    Task<string> GenerateDocumentNumberAsync();

    /// <summary>
    /// Aktualisiert den OCR-Rohtext eines Belegs.
    /// </summary>
    Task UpdateOcrTextAsync(Guid id, string ocrText);

    /// <summary>
    /// Markiert mehrere Belege als bereits an Rechnung angehängt.
    /// </summary>
    Task MarkAsAttachedAsync(IEnumerable<Guid> documentIds);

    /// <summary>
    /// Lädt alle Projekt-Zuteilungen eines Belegs.
    /// </summary>
    Task<List<DocumentAllocationDto>> GetAllocationsAsync(Guid documentId);

    /// <summary>
    /// Setzt die Projekt-Zuteilungen eines Belegs atomisch.
    /// Die Summe der Brutto-Zuteilungen darf den Gesamtbetrag nicht überschreiten.
    /// Nur bei Status Draft erlaubt.
    /// </summary>
    Task SetAllocationsAsync(Guid documentId, IEnumerable<SetAllocationDto> allocations);

    /// <summary>
    /// Setzt das SkontoApplied-Flag unabhängig vom Beleg-Status.
    /// </summary>
    Task SetSkontoAppliedAsync(Guid documentId, bool applied);

    /// <summary>
    /// Lädt das Änderungsprotokoll (GoBD-Audit-Log) eines Belegs, neueste zuerst.
    /// </summary>
    Task<List<AuditLogEntryDto>> GetAuditLogAsync(Guid id);

    /// <summary>
    /// Rechnet die Audit-Log-Hashkette von Genesis bis zum letzten Eintrag durch und meldet den
    /// ersten Bruch (GoBD-Nachweis der Unveränderbarkeit).
    /// </summary>
    Task<AuditHashChain.ChainVerificationResult> VerifyAuditLogChainAsync();
}
