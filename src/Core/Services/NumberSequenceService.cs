using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kuestencode.Core.Services;

/// <summary>
/// Atomarer, pro Schema laufender Zähler für Belegnummern (Rechnungen, Eingangsrechnungen, ...).
/// Ersetzt das bisherige "max(vorhandene Nummern)+1"-Scanning, das bei parallelen Requests
/// zu Race Conditions/Lücken führen konnte. Jedes Modul übergibt seine eigene, schema-qualifizierte
/// Tabelle (z.B. faktura."NumberSequences") — keine Cross-Schema-Kopplung.
/// </summary>
public static class NumberSequenceService
{
    /// <summary>
    /// Liefert die nächste Nummer für <paramref name="sequenceKey"/> und persistiert den neuen
    /// Zählerstand atomar (SELECT ... FOR UPDATE). Existiert noch keine Zeile für den Schlüssel,
    /// wird sie einmalig mit dem Ergebnis von <paramref name="seedFromExistingAsync"/> angelegt
    /// (z.B. der bisherige In-Memory-Scan), ohne bestehende Nummern zu verändern.
    /// </summary>
    public static async Task<string> GetNextNumberAsync(
        DatabaseFacade database,
        string schemaQualifiedTable,
        string sequenceKey,
        string format,
        DateTime referenceDate,
        Func<Task<long>> seedFromExistingAsync,
        CancellationToken ct = default)
    {
        if (!database.IsRelational())
        {
            // Nicht-relationale Provider (z.B. EF Core InMemory in Tests) kennen weder
            // Transaktionen noch rohes SQL. Fallback auf den bisherigen Scan-Ansatz ohne
            // Atomaritätsgarantie - im Produktivbetrieb läuft ausschließlich Npgsql (relational).
            var seed = await seedFromExistingAsync();
            return DocumentNumberFormatter.Format(format, referenceDate, seed + 1);
        }

        var connection = database.GetDbConnection();
        var connectionWasOpen = connection.State == ConnectionState.Open;
        if (!connectionWasOpen)
            await connection.OpenAsync(ct);

        try
        {
            var transaction = await database.BeginTransactionAsync(ct);
            try
            {
                var current = await TryLockCurrentValueAsync(connection, transaction.GetDbTransaction(), schemaQualifiedTable, sequenceKey, ct);

                if (current == null)
                {
                    // Zeile existiert noch nicht: einmalig seeden. ON CONFLICT DO NOTHING schließt die
                    // Race zwischen zwei parallelen Requests, die beide keine Zeile vorfinden — danach
                    // existiert garantiert genau eine Zeile, die der nachfolgende Re-Select sperrt.
                    var seed = await seedFromExistingAsync();
                    await InsertSeedAsync(connection, transaction.GetDbTransaction(), schemaQualifiedTable, sequenceKey, seed, ct);
                    current = await TryLockCurrentValueAsync(connection, transaction.GetDbTransaction(), schemaQualifiedTable, sequenceKey, ct)
                        ?? throw new InvalidOperationException($"NumberSequence '{sequenceKey}' konnte nicht angelegt werden.");
                }

                var next = current.Value + 1;
                await UpdateCurrentValueAsync(connection, transaction.GetDbTransaction(), schemaQualifiedTable, sequenceKey, next, ct);

                await transaction.CommitAsync(ct);
                return DocumentNumberFormatter.Format(format, referenceDate, next);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }
        finally
        {
            if (!connectionWasOpen)
                await connection.CloseAsync();
        }
    }

    private static async Task<long?> TryLockCurrentValueAsync(
        DbConnection connection, DbTransaction transaction, string schemaQualifiedTable, string sequenceKey, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT \"CurrentValue\" FROM {schemaQualifiedTable} WHERE \"SequenceKey\" = @key FOR UPDATE";
        AddParam(command, "@key", sequenceKey);

        var result = await command.ExecuteScalarAsync(ct);
        return result == null ? null : Convert.ToInt64(result);
    }

    private static async Task InsertSeedAsync(
        DbConnection connection, DbTransaction transaction, string schemaQualifiedTable, string sequenceKey, long seed, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {schemaQualifiedTable} ("Id", "SequenceKey", "CurrentValue", "UpdatedAt")
            VALUES (gen_random_uuid(), @key, @seed, now())
            ON CONFLICT ("SequenceKey") DO NOTHING
            """;
        AddParam(command, "@key", sequenceKey);
        AddParam(command, "@seed", seed);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpdateCurrentValueAsync(
        DbConnection connection, DbTransaction transaction, string schemaQualifiedTable, string sequenceKey, long next, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {schemaQualifiedTable} SET \"CurrentValue\" = @next, \"UpdatedAt\" = now() WHERE \"SequenceKey\" = @key";
        AddParam(command, "@next", next);
        AddParam(command, "@key", sequenceKey);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddParam(DbCommand command, string name, object value)
    {
        var param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        command.Parameters.Add(param);
    }
}
