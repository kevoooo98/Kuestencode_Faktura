using FluentAssertions;
using Kuestencode.Core.Auditing;
using Xunit;

namespace Kuestencode.Core.Tests.Auditing;

public class AuditHashChainTests
{
    private sealed record TestEntry(
        long SequenceNumber, string EntityName, string EntityId, string Action,
        string? FieldName, string? OldValue, string? NewValue,
        Guid ChangedByUserId, string ChangedByUserName, DateTime ChangedAt,
        string Hash, string PreviousHash) : IChainedAuditEntry;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime BaseTime =
        AuditHashChain.TruncateToPostgresPrecision(new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc));

    private static TestEntry MakeChainedEntry(
        long sequenceNumber, string previousHash, string fieldName = "Status",
        string? oldValue = "Draft", string? newValue = "Sent")
    {
        var hash = AuditHashChain.ComputeHash(
            previousHash, "Invoice", "1", "Modified", fieldName, oldValue, newValue,
            UserId, "Test User", BaseTime);

        return new TestEntry(sequenceNumber, "Invoice", "1", "Modified", fieldName, oldValue, newValue,
            UserId, "Test User", BaseTime, hash, previousHash);
    }

    // ─── ComputeHash: Kollisionssicherheit ─────────────────────────────────────

    [Fact]
    public void ComputeHash_FeldwertMitTrennzeichenAehnlichemInhalt_KollidiertNichtMitAndererAufteilung()
    {
        // Naives string.Join('|', ...) würde für beide Fälle dieselbe Payload "a|b|c" erzeugen:
        // Fall 1: FieldName="a", OldValue="b|c"   -> "a|b|c"
        // Fall 2: FieldName="a|b", OldValue="c"   -> "a|b|c"
        var hash1 = AuditHashChain.ComputeHash(
            AuditHashChain.Genesis, "Invoice", "1", "Modified", "a", "b|c", null,
            UserId, "Test User", BaseTime);

        var hash2 = AuditHashChain.ComputeHash(
            AuditHashChain.Genesis, "Invoice", "1", "Modified", "a|b", "c", null,
            UserId, "Test User", BaseTime);

        hash1.Should().NotBe(hash2, "unterschiedliche Feldaufteilungen dürfen niemals denselben Hash ergeben");
    }

    [Fact]
    public void ComputeHash_NullFeld_UnterscheidetSichVonLeeremString()
    {
        var hashNull = AuditHashChain.ComputeHash(
            AuditHashChain.Genesis, "Invoice", "1", "Created", null, null, null,
            UserId, "Test User", BaseTime);

        var hashEmpty = AuditHashChain.ComputeHash(
            AuditHashChain.Genesis, "Invoice", "1", "Created", "", "", "",
            UserId, "Test User", BaseTime);

        hashNull.Should().NotBe(hashEmpty);
    }

    [Fact]
    public void ComputeHash_IstDeterministisch()
    {
        var hash1 = AuditHashChain.ComputeHash(
            AuditHashChain.Genesis, "Invoice", "1", "Modified", "Status", "Draft", "Sent",
            UserId, "Test User", BaseTime);

        var hash2 = AuditHashChain.ComputeHash(
            AuditHashChain.Genesis, "Invoice", "1", "Modified", "Status", "Draft", "Sent",
            UserId, "Test User", BaseTime);

        hash1.Should().Be(hash2);
    }

    // ─── TruncateToPostgresPrecision ────────────────────────────────────────────

    [Fact]
    public void TruncateToPostgresPrecision_EntferntSubMikrosekundenTicks()
    {
        // 1234567 Ticks = 123.4567 ms; die letzte Stelle (7) liegt unterhalb der Mikrosekunden-Grenze.
        var withSubMicroTicks = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567);

        var truncated = AuditHashChain.TruncateToPostgresPrecision(withSubMicroTicks);

        (truncated.Ticks % 10).Should().Be(0, "Postgres speichert nur Mikrosekunden (10-Tick-Vielfache)");
        truncated.Should().BeOnOrBefore(withSubMicroTicks);
        (withSubMicroTicks - truncated).Should().BeLessThan(TimeSpan.FromTicks(10));
    }

    [Fact]
    public void TruncateToPostgresPrecision_IstIdempotent()
    {
        var value = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc).AddTicks(4567);

        var once = AuditHashChain.TruncateToPostgresPrecision(value);
        var twice = AuditHashChain.TruncateToPostgresPrecision(once);

        twice.Should().Be(once, "ein bereits trunkierter Wert (z.B. nach einem DB-Roundtrip) darf sich nicht weiter verschieben");
    }

    // ─── VerifyChain ────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyChain_LeereKette_IstGueltig()
    {
        var result = AuditHashChain.VerifyChain(Array.Empty<TestEntry>());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void VerifyChain_IntakteMehrgliedrigeKette_IstGueltig()
    {
        var entry1 = MakeChainedEntry(1, AuditHashChain.Genesis);
        var entry2 = MakeChainedEntry(2, entry1.Hash);
        var entry3 = MakeChainedEntry(3, entry2.Hash);

        var result = AuditHashChain.VerifyChain(new[] { entry1, entry2, entry3 });

        result.IsValid.Should().BeTrue();
        result.BrokenAtSequenceNumber.Should().BeNull();
    }

    [Fact]
    public void VerifyChain_InhaltEinerZeileNachtraeglichVeraendert_MeldetGenauDieseZeileAlsErstenBruch()
    {
        var entry1 = MakeChainedEntry(1, AuditHashChain.Genesis);
        var entry2 = MakeChainedEntry(2, entry1.Hash);
        var entry3 = MakeChainedEntry(3, entry2.Hash);

        // Simuliert eine Manipulation per direktem DB-Zugriff: NewValue wurde geändert, Hash/PreviousHash
        // blieben (wie bei einem echten UPDATE ohne Neuberechnung) unverändert.
        var manipuliert = entry2 with { NewValue = "Cancelled" };

        var result = AuditHashChain.VerifyChain(new[] { entry1, manipuliert, entry3 });

        result.IsValid.Should().BeFalse();
        result.BrokenAtSequenceNumber.Should().Be(2, "genau die manipulierte Zeile muss als erster Bruch erkannt werden");
    }

    [Fact]
    public void VerifyChain_ZeileAusgeschnittenOderUmsortiert_MeldetBruchAnPreviousHash()
    {
        var entry1 = MakeChainedEntry(1, AuditHashChain.Genesis);
        var entry2 = MakeChainedEntry(2, entry1.Hash);
        var entry3 = MakeChainedEntry(3, entry2.Hash);

        // entry2 entfernt: entry3.PreviousHash zeigt jetzt auf eine nicht mehr vorhandene Vorgänger-Zeile.
        var result = AuditHashChain.VerifyChain(new[] { entry1, entry3 });

        result.IsValid.Should().BeFalse();
        result.BrokenAtSequenceNumber.Should().Be(3);
    }

    [Fact]
    public void VerifyChain_ErsteZeileOhneGenesisAlsPreviousHash_WirdAlsBruchErkannt()
    {
        var faelschlich = MakeChainedEntry(1, "irgendein-anderer-hash");

        var result = AuditHashChain.VerifyChain(new[] { faelschlich });

        result.IsValid.Should().BeFalse();
        result.BrokenAtSequenceNumber.Should().Be(1);
    }

    // ─── Legacy-Zeilen aus der Zeit vor Einführung der Hashkette ───────────────

    private static TestEntry MakeLegacyEntry(long sequenceNumber, string newValue = "Alter Wert") =>
        new(sequenceNumber, "Invoice", "1", "Modified", "Status", "Draft", newValue,
            UserId, "Test User", BaseTime, AuditHashChain.Genesis, AuditHashChain.Genesis);

    [Fact]
    public void VerifyChain_LegacyZeileVorEinfuehrungDerKette_WirdNichtAlsManipulationGemeldet()
    {
        // Migration AddAuditLogHashChain setzt für bereits vorhandene Zeilen Hash=Genesis als
        // Platzhalter, ohne den Inhalt rückwirkend zu hashen. VerifyChain darf diese Zeilen nicht
        // als "manipuliert" melden, sonst löst jedes Upgrade eines bereits produktiven Systems
        // (Audit-Log ohne Hashkette -> mit Hashkette) einen falschen Manipulationsalarm auf echten,
        // nie angefassten historischen Daten aus.
        var legacy1 = MakeLegacyEntry(1);
        var legacy2 = MakeLegacyEntry(2, "Noch ein alter Wert");
        var real3 = MakeChainedEntry(3, AuditHashChain.Genesis);

        var result = AuditHashChain.VerifyChain(new[] { legacy1, legacy2, real3 });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void VerifyChain_EchteZeileAlsLegacyGetarnt_BruchZeigtSichAnDerVerkettungZurNachfolgezeile()
    {
        // Ein Angreifer mit direktem DB-Zugriff könnte versuchen, eine bereits echt verkettete Zeile
        // zu manipulieren und ihren eigenen Hash auf Genesis zurückzusetzen, um die Inhaltsprüfung zu
        // umgehen (siehe VerifyChain: Hash==Genesis überspringt die Inhaltsprüfung). Das darf nicht
        // unbemerkt bleiben: die Nachfolgezeile referenziert per PreviousHash weiterhin den echten,
        // ursprünglichen Hash — die Verkettung bricht daher spätestens dort sichtbar.
        var entry1 = MakeChainedEntry(1, AuditHashChain.Genesis);
        var entry2 = MakeChainedEntry(2, entry1.Hash);
        var entry3 = MakeChainedEntry(3, entry2.Hash);

        var getarnt = entry2 with { NewValue = "Cancelled", Hash = AuditHashChain.Genesis };

        var result = AuditHashChain.VerifyChain(new[] { entry1, getarnt, entry3 });

        result.IsValid.Should().BeFalse("die Verkettung zu entry3 bleibt inkonsistent, auch wenn die getarnte Zeile selbst ungeprüft durchläuft");
        result.BrokenAtSequenceNumber.Should().Be(3);
    }
}
