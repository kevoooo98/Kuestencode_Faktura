namespace Kuestencode.Faktura.Models;

/// <summary>
/// Eine einzelne, unveränderliche Änderungsprotokoll-Zeile (GoBD). Wird ausschließlich durch
/// <see cref="Data.FakturaDbContext.SaveChangesAsync"/> geschrieben, nie manuell.
/// </summary>
public class AuditLogEntry
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
}
