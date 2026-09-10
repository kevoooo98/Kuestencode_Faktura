using Kuestencode.Core.Auditing;

namespace Kuestencode.Werkbank.Recepta.Domain.Entities;

/// <summary>
/// Eine einzelne, unveränderliche Änderungsprotokoll-Zeile (GoBD). Wird ausschließlich durch
/// <see cref="Data.ReceptaDbContext.SaveChangesAsync"/> geschrieben, nie manuell.
/// </summary>
public class AuditLogEntry : IChainedAuditEntry
{
    public Guid Id { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public Guid ChangedByUserId { get; set; }
    public string ChangedByUserName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }

    /// <summary>Fortlaufende Nummer (DB-Identity), bestimmt die Reihenfolge der Hashkette.</summary>
    public long SequenceNumber { get; set; }

    /// <summary>SHA-256-Hash dieser Zeile (Inhalt + PreviousHash) — siehe AuditHashChain.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>Hash der vorherigen Zeile, oder AuditHashChain.Genesis für die erste Zeile.</summary>
    public string PreviousHash { get; set; } = string.Empty;
}
