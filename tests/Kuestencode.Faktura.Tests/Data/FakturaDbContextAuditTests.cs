using System.Globalization;
using FluentAssertions;
using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kuestencode.Faktura.Tests.Data;

/// <summary>
/// Prüft den SaveChanges-Interceptor in FakturaDbContext, der Änderungen an Invoice-Feldern
/// automatisch ins Audit-Log schreibt (GoBD) — unabhängig vom Aufrufer (Service, Test, ...).
/// </summary>
public class FakturaDbContextAuditTests
{
    private static FakturaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FakturaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FakturaDbContext(options);
    }

    private static Invoice MakeInvoice() => new()
    {
        InvoiceNumber = "R-2026-0001",
        InvoiceDate = DateTime.UtcNow,
        CustomerId = 1,
        Status = InvoiceStatus.Draft
    };

    [Fact]
    public async Task SaveChanges_NeueRechnung_ErzeugtCreatedEintrag()
    {
        await using var context = CreateContext();
        var invoice = MakeInvoice();

        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var entries = await context.AuditLogEntries.ToListAsync();
        entries.Should().ContainSingle(e =>
            e.EntityName == nameof(Invoice) &&
            e.EntityId == invoice.Id.ToString() &&
            e.Action == "Created");
    }

    [Fact]
    public async Task SaveChanges_StatusGeaendert_ErzeugtModifiedEintragMitAltUndNeu()
    {
        await using var context = CreateContext();
        var invoice = MakeInvoice();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        invoice.Status = InvoiceStatus.Sent;
        await context.SaveChangesAsync();

        var entry = await context.AuditLogEntries
            .SingleAsync(e => e.Action == "Modified" && e.FieldName == "Status");

        entry.OldValue.Should().Be("Draft");
        entry.NewValue.Should().Be("Sent");
        entry.EntityId.Should().Be(invoice.Id.ToString());
    }

    [Fact]
    public async Task SaveChanges_UngelistetesFeldGeaendert_ErzeugtKeinenEintrag()
    {
        await using var context = CreateContext();
        var invoice = MakeInvoice();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
        var countAfterCreate = await context.AuditLogEntries.CountAsync();

        // InvoiceNumber ist NICHT auf der Whitelist (nur Status/InvoiceDate/DueDate/Notes/...)
        invoice.InvoiceNumber = "R-2026-9999";
        await context.SaveChangesAsync();

        var countAfterRename = await context.AuditLogEntries.CountAsync();
        countAfterRename.Should().Be(countAfterCreate);
    }

    [Fact]
    public async Task SaveChanges_ZahlungErfasst_ErzeugtCreatedEintragUnterRechnung()
    {
        await using var context = CreateContext();
        var invoice = MakeInvoice();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var amount = 119.00m;
        var payment = new InvoicePayment { InvoiceId = invoice.Id, Amount = amount, PaymentDate = DateTime.UtcNow };
        context.InvoicePayments.Add(payment);
        await context.SaveChangesAsync();

        var entries = await context.AuditLogEntries
            .Where(e => e.EntityName == nameof(Invoice) && e.EntityId == invoice.Id.ToString() && e.Action == "Created" && e.FieldName == "Amount")
            .ToListAsync();

        // Der Interceptor formatiert Werte kulturunabhängig (InvariantCulture) fürs Audit-Log,
        // damit der Trail unabhängig von der Server-Kultur konsistent bleibt (siehe AuditChangeCollector).
        entries.Should().ContainSingle(e => e.NewValue == amount.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SaveChanges_ZahlungGeloescht_ErzeugtDeletedEintragMitBetrag()
    {
        await using var context = CreateContext();
        var invoice = MakeInvoice();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var amount = 50.00m;
        var payment = new InvoicePayment { InvoiceId = invoice.Id, Amount = amount, PaymentDate = DateTime.UtcNow };
        context.InvoicePayments.Add(payment);
        await context.SaveChangesAsync();

        context.InvoicePayments.Remove(payment);
        await context.SaveChangesAsync();

        var entry = await context.AuditLogEntries
            .SingleAsync(e => e.Action == "Deleted" && e.FieldName == "Amount");

        entry.EntityName.Should().Be(nameof(Invoice));
        entry.EntityId.Should().Be(invoice.Id.ToString());
        entry.OldValue.Should().Be(amount.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SaveChanges_RechnungGeloescht_ErzeugtDeletedEintrag()
    {
        await using var context = CreateContext();
        var invoice = MakeInvoice();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        context.Invoices.Remove(invoice);
        await context.SaveChangesAsync();

        var entries = await context.AuditLogEntries.Where(e => e.Action == "Deleted").ToListAsync();
        entries.Should().ContainSingle(e => e.EntityId == invoice.Id.ToString());
    }

    // ─── Hashkette (GoBD-Nachweisbarkeit) ──────────────────────────────────────

    [Fact]
    public async Task SaveChanges_ErsteZeile_HatGenesisAlsPreviousHash()
    {
        await using var context = CreateContext();
        var invoice = MakeInvoice();

        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var entry = await context.AuditLogEntries.SingleAsync();
        entry.PreviousHash.Should().Be(Kuestencode.Core.Auditing.AuditHashChain.Genesis);
        entry.Hash.Should().NotBeNullOrEmpty().And.NotBe(entry.PreviousHash);
    }

    [Fact]
    public async Task SaveChanges_MehrereZeilen_SindLueckenlosVerkettet()
    {
        await using var context = CreateContext();
        var invoice = MakeInvoice();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        invoice.Status = InvoiceStatus.Sent;
        await context.SaveChangesAsync();
        invoice.Notes = "Testnotiz";
        await context.SaveChangesAsync();

        var entries = await context.AuditLogEntries
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync();

        entries.Should().HaveCount(3);
        for (var i = 1; i < entries.Count; i++)
        {
            entries[i].PreviousHash.Should().Be(entries[i - 1].Hash,
                "jede Zeile muss den Hash ihrer Vorgänger-Zeile referenzieren");
        }
    }

    [Fact]
    public async Task SaveChanges_HashIstManipulationssensitiv_VeraenderterInhaltErgibtAnderenHash()
    {
        await using var context = CreateContext();
        var invoice = MakeInvoice();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        var entry = await context.AuditLogEntries.SingleAsync();

        var recomputed = Kuestencode.Core.Auditing.AuditHashChain.ComputeHash(
            entry.PreviousHash, entry.EntityName, entry.EntityId, entry.Action,
            entry.FieldName, entry.OldValue, entry.NewValue,
            entry.ChangedByUserId, entry.ChangedByUserName, entry.ChangedAt);
        recomputed.Should().Be(entry.Hash, "unveränderter Inhalt muss denselben Hash reproduzieren");

        // Simuliert eine nachträgliche Manipulation per direktem DB-Zugriff (z.B. UPDATE):
        var manipulatedHash = Kuestencode.Core.Auditing.AuditHashChain.ComputeHash(
            entry.PreviousHash, entry.EntityName, entry.EntityId, entry.Action,
            entry.FieldName, entry.OldValue, newValue: "manipuliert",
            entry.ChangedByUserId, entry.ChangedByUserName, entry.ChangedAt);
        manipulatedHash.Should().NotBe(entry.Hash, "ein veränderter Inhalt muss einen abweichenden Hash ergeben");
    }
}
