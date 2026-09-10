using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kuestencode.Core.Auditing;

/// <summary>
/// Verkettet Audit-Log-Zeilen per SHA-256-Hashkette (jede Zeile hasht ihren eigenen Inhalt
/// zusammen mit dem Hash der Vorgänger-Zeile). Ein direkter UPDATE/DELETE auf der Tabelle per
/// DB-Zugriff (z.B. durch den Betreiber selbst) bleibt technisch möglich — die Kette macht eine
/// solche Manipulation aber ab dem veränderten Eintrag nachweisbar inkonsistent. Das ist der
/// eigentliche GoBD-Punkt: nicht absolute Verhinderung, sondern Nachweisbarkeit.
/// </summary>
public static class AuditHashChain
{
    /// <summary>Hash-Wert der (nicht existierenden) Vorgänger-Zeile des allerersten Eintrags.</summary>
    public static readonly string Genesis = new('0', 64);

    /// <summary>
    /// Berechnet den Hash einer Zeile aus ihrem Inhalt + dem Hash der Vorgänger-Zeile.
    /// Werte werden nicht (mehr) über kulturabhängiges ToString() eingebettet — die aufrufende
    /// Stelle liefert bereits invariant formatierte Strings (siehe AuditChangeCollector).
    /// </summary>
    public static string ComputeHash(
        string previousHash, string entityName, string entityId, string action,
        string? fieldName, string? oldValue, string? newValue,
        Guid changedByUserId, string changedByUserName, DateTime changedAt)
    {
        var payload = string.Join('|',
            previousHash,
            entityName,
            entityId,
            action,
            fieldName ?? "",
            oldValue ?? "",
            newValue ?? "",
            changedByUserId.ToString(),
            changedByUserName,
            changedAt.ToString("O", CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    /// <summary>Letzter Zustand der Kette: laufende Nummer und Hash der zuletzt geschriebenen Zeile.</summary>
    public readonly record struct ChainTip(long SequenceNumber, string Hash);

    /// <summary>
    /// Liest Nummer und Hash der zuletzt geschriebenen Zeile per <c>SELECT ... FOR UPDATE</c> und
    /// sperrt sie damit für die Dauer der aufrufenden Transaktion — verhindert, dass zwei parallele
    /// Requests dieselbe "letzte" Zeile sehen und die Kette gabeln. Der Aufrufer muss bereits eine
    /// Transaktion auf <paramref name="database"/> begonnen haben, da der nachfolgende EF-Core-Insert
    /// der neuen Zeile(n) in derselben Transaktion laufen muss. Gibt null zurück, wenn die Tabelle
    /// noch leer ist (Kettenanfang). SequenceNumber wird bewusst applikationsseitig statt per
    /// DB-Identity vergeben — konsistentes Verhalten über relationale und nicht-relationale
    /// Provider hinweg (EF InMemory generiert Werte nur für Key-Properties zuverlässig).
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
