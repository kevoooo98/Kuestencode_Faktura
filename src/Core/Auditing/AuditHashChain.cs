using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kuestencode.Core.Auditing;

/// <summary>
/// Ein Audit-Log-Eintrag, wie ihn die Kettenverifikation braucht — implementiert von
/// Faktura.Models.AuditLogEntry und Recepta.Domain.Entities.AuditLogEntry (identische Form,
/// bewusst kein gemeinsamer Basistyp, da beide Module unabhängig bleiben sollen).
/// </summary>
public interface IChainedAuditEntry
{
    long SequenceNumber { get; }
    string EntityName { get; }
    string EntityId { get; }
    string Action { get; }
    string? FieldName { get; }
    string? OldValue { get; }
    string? NewValue { get; }
    Guid ChangedByUserId { get; }
    string ChangedByUserName { get; }
    DateTime ChangedAt { get; }
    string Hash { get; }
    string PreviousHash { get; }
}

/// <summary>
/// Verkettet Audit-Log-Zeilen per SHA-256-Hashkette (jede Zeile hasht ihren eigenen Inhalt
/// zusammen mit dem Hash der Vorgänger-Zeile). Ein direkter UPDATE/DELETE auf der Tabelle per
/// DB-Zugriff (z.B. durch den Betreiber selbst) bleibt technisch möglich — die Kette macht eine
/// solche Manipulation aber ab dem veränderten Eintrag nachweisbar inkonsistent. Das ist der
/// eigentliche GoBD-Punkt: nicht absolute Verhinderung, sondern Nachweisbarkeit — und genau
/// deshalb ist <see cref="VerifyChain"/> der eigentliche Zweck dieser Klasse, nicht nur ein
/// Nebenprodukt des Schreibens.
/// </summary>
public static class AuditHashChain
{
    /// <summary>Hash-Wert der (nicht existierenden) Vorgänger-Zeile des allerersten Eintrags.</summary>
    public static readonly string Genesis = new('0', 64);

    /// <summary>
    /// Postgres' "timestamp with time zone" speichert nur Mikrosekunden (6 Nachkommastellen),
    /// .NET-DateTime-Ticks sind 100ns-genau (7 Nachkommastellen). Ohne Trunkierung würde die beim
    /// Schreiben gehashte Zeit von der nach einem Roundtrip aus der DB gelesenen Zeit abweichen
    /// (letzte Tick-Stelle verloren) — ein Verify direkt nach dem Neuladen der Zeile würde dann
    /// fälschlich einen Bruch melden, obwohl nichts manipuliert wurde. Muss VOR dem Hashen UND vor
    /// dem Speichern angewendet werden, damit gehashter und persistierter Wert bitgleich sind.
    /// </summary>
    public static DateTime TruncateToPostgresPrecision(DateTime value)
    {
        const long ticksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000; // 10
        var truncatedTicks = value.Ticks - (value.Ticks % ticksPerMicrosecond);
        return new DateTime(truncatedTicks, value.Kind);
    }

    /// <summary>
    /// Berechnet den Hash einer Zeile aus ihrem Inhalt + dem Hash der Vorgänger-Zeile.
    /// Jedes Feld wird längenpräfixiert eingebettet (statt mit '|' verklebt), damit ein Feldwert,
    /// der selbst einen Trennzeichen-artigen Inhalt enthält (z.B. Notes mit einem "|"), die Payload
    /// nicht mehrdeutig macht — zwei unterschiedliche Feldbelegungen dürfen niemals denselben Hash
    /// ergeben können. <paramref name="changedAt"/> muss bereits via
    /// <see cref="TruncateToPostgresPrecision"/> normalisiert sein.
    /// </summary>
    public static string ComputeHash(
        string previousHash, string entityName, string entityId, string action,
        string? fieldName, string? oldValue, string? newValue,
        Guid changedByUserId, string changedByUserName, DateTime changedAt)
    {
        var sb = new StringBuilder();
        AppendField(sb, previousHash);
        AppendField(sb, entityName);
        AppendField(sb, entityId);
        AppendField(sb, action);
        AppendField(sb, fieldName);
        AppendField(sb, oldValue);
        AppendField(sb, newValue);
        AppendField(sb, changedByUserId.ToString());
        AppendField(sb, changedByUserName);
        AppendField(sb, changedAt.ToString("O", CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Hängt ein Feld längenpräfixiert an (z.B. "5:hallo"). null wird als eigenes Präfix "-1:"
    /// kodiert, damit null und "" (Länge 0) nicht kollidieren können.
    /// </summary>
    private static void AppendField(StringBuilder sb, string? value)
    {
        if (value == null)
        {
            sb.Append("-1:");
        }
        else
        {
            sb.Append(value.Length).Append(':').Append(value);
        }
    }

    /// <summary>Ergebnis einer Kettenprüfung: entweder vollständig intakt, oder der erste Bruch.</summary>
    public sealed record ChainVerificationResult(bool IsValid, long? BrokenAtSequenceNumber, string? Reason)
    {
        public static readonly ChainVerificationResult Valid = new(true, null, null);
    }

    /// <summary>
    /// Rechnet die Kette von Genesis bis zum letzten Eintrag durch und meldet den ERSTEN Bruch —
    /// entweder weil der gespeicherte Hash nicht mehr zum (unveränderten) Inhalt der Zeile passt
    /// (die Zeile selbst wurde verändert) oder weil PreviousHash nicht mit dem Hash der
    /// Vorgänger-Zeile übereinstimmt (eine Zeile wurde entfernt/eingefügt/umsortiert). Das ist der
    /// eigentliche Nachweis, für den die Kette existiert — ohne diese Funktion sind Hash/PreviousHash
    /// nur gespeicherte Daten ohne Beweiskraft. <paramref name="entriesInOrder"/> muss aufsteigend
    /// nach SequenceNumber sortiert sein und lückenlos ab dem ersten Eintrag beginnen (bei einem
    /// Teilausschnitt lässt sich Manipulation vor dem ersten übergebenen Eintrag nicht erkennen).
    /// </summary>
    public static ChainVerificationResult VerifyChain(IEnumerable<IChainedAuditEntry> entriesInOrder)
    {
        var expectedPrevious = Genesis;

        foreach (var entry in entriesInOrder)
        {
            if (entry.PreviousHash != expectedPrevious)
            {
                return new ChainVerificationResult(false, entry.SequenceNumber,
                    $"PreviousHash bei SequenceNumber {entry.SequenceNumber} stimmt nicht mit dem Hash der Vorgänger-Zeile überein — Zeile wurde eingefügt, entfernt oder umsortiert.");
            }

            // Zeilen aus der Zeit vor Einführung der Hashkette (Migration AddAuditLogHashChain)
            // tragen den Platzhalter Genesis als eigenen Hash — für sie gibt es keinen echten Hash
            // gegen den Inhalt zu prüfen. Die Linkage-Prüfung oben bleibt trotzdem immer aktiv: eine
            // echte, bereits verkettete Zeile lässt sich nicht nachträglich als "Legacy" tarnen, ohne
            // die Verkettung zu ihrer wahren Vorgänger- oder Nachfolgezeile zu brechen.
            if (entry.Hash != Genesis)
            {
                var recomputed = ComputeHash(
                    entry.PreviousHash, entry.EntityName, entry.EntityId, entry.Action,
                    entry.FieldName, entry.OldValue, entry.NewValue,
                    entry.ChangedByUserId, entry.ChangedByUserName, entry.ChangedAt);

                if (recomputed != entry.Hash)
                {
                    return new ChainVerificationResult(false, entry.SequenceNumber,
                        $"Der Inhalt der Zeile mit SequenceNumber {entry.SequenceNumber} wurde nach dem Schreiben verändert (Hash passt nicht mehr zum gespeicherten Inhalt).");
                }
            }

            expectedPrevious = entry.Hash;
        }

        return ChainVerificationResult.Valid;
    }

    /// <summary>Letzter Zustand der Kette: laufende Nummer und Hash der zuletzt geschriebenen Zeile.</summary>
    public readonly record struct ChainTip(long SequenceNumber, string Hash);

    /// <summary>
    /// Liest Nummer und Hash der zuletzt geschriebenen Zeile per <c>SELECT ... FOR UPDATE</c> und
    /// sperrt sie damit für die Dauer der aufrufenden Transaktion — verhindert, dass zwei parallele
    /// Requests dieselbe "letzte" Zeile sehen und die Kette gabeln. Der Aufrufer muss bereits eine
    /// Transaktion auf <paramref name="database"/> begonnen haben, da der nachfolgende EF-Core-Insert
    /// der neuen Zeile(n) in derselben Transaktion laufen muss. Gibt null zurück, wenn die Tabelle
    /// noch leer ist (Kettenanfang) — für genau diesen Fall gibt es keine Zeile zu sperren; ein
    /// gleichzeitiger zweiter "erster" Schreiber wird stattdessen vom Unique-Index auf
    /// SequenceNumber abgefangen (siehe Aufrufer: Retry-on-Conflict). SequenceNumber wird bewusst
    /// applikationsseitig statt per DB-Identity vergeben — konsistentes Verhalten über relationale
    /// und nicht-relationale Provider hinweg (EF InMemory generiert Werte nur für Key-Properties
    /// zuverlässig).
    /// </summary>
    public static async Task<ChainTip?> GetTipLockedAsync(
        DatabaseFacade database, string schemaQualifiedTable, CancellationToken ct = default)
    {
        var connection = database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        var transaction = database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "AuditHashChain.GetTipLockedAsync erfordert eine bereits offene Transaktion, " +
                "damit der nachfolgende Insert der neuen Kette denselben Sperr-Kontext nutzt.");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            $"SELECT \"SequenceNumber\", \"Hash\" FROM {schemaQualifiedTable} ORDER BY \"SequenceNumber\" DESC LIMIT 1 FOR UPDATE";

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new ChainTip(reader.GetInt64(0), reader.GetString(1));
    }
}
