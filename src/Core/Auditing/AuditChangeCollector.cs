using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Kuestencode.Core.Auditing;

/// <summary>
/// Konfiguriert, wie ein Entity-Typ im Audit-Log erfasst wird.
/// </summary>
/// <param name="Fields">Felder, deren Änderungen protokolliert werden.</param>
/// <param name="ParentIdProperty">
/// Für Kind-Entities (z.B. InvoicePayment), die im Audit-Log unter ihrem Eltern-Datensatz
/// erscheinen sollen: Name der FK-Property, deren Wert als EntityId verwendet wird, statt der
/// eigenen Id. Null = die eigene "Id"-Property wird verwendet (Standardfall).
/// </param>
/// <param name="ParentEntityName">EntityName-Override für Kind-Entities, z.B. "Invoice" statt "InvoicePayment". Null = eigener Typname.</param>
/// <param name="DetailedAddDelete">
/// Bei true werden Added/Deleted nicht als leere Zusammenfassungszeile, sondern als eine Zeile
/// je gelistetem, nicht-null Feld erfasst — sinnvoll für Entities, die nie geändert werden
/// (nur angelegt/gelöscht, z.B. Zahlungen), damit z.B. der Betrag sichtbar bleibt.
/// </param>
public sealed record AuditedEntityConfig(
    string[] Fields,
    string? ParentIdProperty = null,
    string? ParentEntityName = null,
    bool DetailedAddDelete = false);

/// <summary>
/// Eine noch nicht persistierte Audit-Zeile, gesammelt aus dem ChangeTracker vor SaveChanges.
/// Die Entity-Referenz wird mitgeführt, damit nach dem Speichern (bei DB-generierten Ids wie
/// Faktura.Invoice.Id) die tatsächliche Id ausgelesen werden kann, sofern kein
/// <see cref="EntityIdOverride"/> (Kind-Entity mit bereits bekannter Parent-FK) vorliegt.
/// </summary>
public sealed record PendingAuditChange(
    object Entity,
    string EntityName,
    string? EntityIdOverride,
    string Action,
    string? FieldName,
    string? OldValue,
    string? NewValue);

/// <summary>
/// Erfasst prüfungsrelevante Änderungen direkt aus dem EF-Core-ChangeTracker, damit sie
/// unabhängig vom Aufrufer (UI, API, Hintergrundjob) lückenlos erfasst werden — im Gegensatz zu
/// einzelnen Log-Aufrufen an verstreuten Call-Sites. Pro Modul wird dies im jeweiligen DbContext
/// via SaveChanges-Override verdrahtet (siehe FakturaDbContext/ReceptaDbContext), inkl. eigener
/// AuditLogEntries-Tabelle im jeweiligen Schema — keine Cross-Schema-Kopplung.
/// </summary>
public static class AuditChangeCollector
{
    /// <summary>
    /// Sammelt alle prüfungsrelevanten Änderungen für Entity-Typen, die in
    /// <paramref name="auditedEntities"/> konfiguriert sind. Bei Modified wird pro tatsächlich
    /// geändertem, gelistetem Feld eine Zeile erzeugt; bei Added/Deleted standardmäßig eine
    /// einzelne Zusammenfassungszeile, oder bei <see cref="AuditedEntityConfig.DetailedAddDelete"/>
    /// eine Zeile je gelistetem, nicht-null Feld.
    /// </summary>
    public static List<PendingAuditChange> CapturePending(
        ChangeTracker tracker, IReadOnlyDictionary<Type, AuditedEntityConfig> auditedEntities)
    {
        var pending = new List<PendingAuditChange>();

        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (!auditedEntities.TryGetValue(entry.Entity.GetType(), out var config))
                continue;

            var entityName = config.ParentEntityName ?? entry.Entity.GetType().Name;
            var entityIdOverride = config.ParentIdProperty != null
                ? entry.Entity.GetType().GetProperty(config.ParentIdProperty)?.GetValue(entry.Entity)?.ToString()
                : null;

            if (entry.State == EntityState.Added)
            {
                if (config.DetailedAddDelete)
                {
                    foreach (var field in config.Fields)
                    {
                        var value = FormatInvariant(entry.Property(field).CurrentValue);
                        if (value == null) continue;

                        pending.Add(new PendingAuditChange(entry.Entity, entityName, entityIdOverride, "Created", field, null, value));
                    }
                }
                else
                {
                    pending.Add(new PendingAuditChange(entry.Entity, entityName, entityIdOverride, "Created", null, null, null));
                }
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                if (config.DetailedAddDelete)
                {
                    foreach (var field in config.Fields)
                    {
                        var value = FormatInvariant(entry.Property(field).OriginalValue);
                        if (value == null) continue;

                        pending.Add(new PendingAuditChange(entry.Entity, entityName, entityIdOverride, "Deleted", field, value, null));
                    }
                }
                else
                {
                    pending.Add(new PendingAuditChange(entry.Entity, entityName, entityIdOverride, "Deleted", null, null, null));
                }
                continue;
            }

            foreach (var field in config.Fields)
            {
                var property = entry.Property(field);
                if (!property.IsModified) continue;

                var oldValue = FormatInvariant(property.OriginalValue);
                var newValue = FormatInvariant(property.CurrentValue);
                if (oldValue == newValue) continue;

                pending.Add(new PendingAuditChange(entry.Entity, entityName, entityIdOverride, "Modified", field, oldValue, newValue));
            }
        }

        return pending;
    }

    /// <summary>
    /// Liefert die EntityId für eine gesammelte Änderung: den <see cref="PendingAuditChange.EntityIdOverride"/>
    /// falls gesetzt (Kind-Entity mit bekannter Parent-FK), sonst die "Id"-Eigenschaft des Entities
    /// per Reflection — funktioniert unabhängig davon, ob der Primärschlüssel ein int (Faktura) oder
    /// ein Guid (Recepta) ist, ohne dass Core die konkreten Modul-Entitäten kennen muss.
    /// </summary>
    public static string GetEntityId(PendingAuditChange change)
    {
        if (change.EntityIdOverride != null) return change.EntityIdOverride;
        return change.Entity.GetType().GetProperty("Id")?.GetValue(change.Entity)?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Formatiert einen Feldwert kulturunabhängig fürs Audit-Log. Ohne dies würde z.B. ein Betrag
    /// je nach Server-Kultur mal mit Komma, mal mit Punkt im Prüf-Trail landen — ein inkonsistent
    /// formatierter Trail ist schwerer als "unverändert" zu verteidigen.
    /// </summary>
    private static string? FormatInvariant(object? value) => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
