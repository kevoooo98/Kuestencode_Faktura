using FluentAssertions;
using Kuestencode.Werkbank.Recepta.Data;
using Kuestencode.Werkbank.Recepta.Domain.Entities;
using Kuestencode.Werkbank.Recepta.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kuestencode.Werkbank.Recepta.Tests.Data;

/// <summary>
/// Prüft den SaveChanges-Interceptor in ReceptaDbContext, der Änderungen an Document-Feldern
/// automatisch ins Audit-Log schreibt (GoBD) — unabhängig vom Aufrufer (Service, Test, ...).
/// </summary>
public class ReceptaDbContextAuditTests
{
    private static ReceptaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ReceptaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ReceptaDbContext(options);
    }

    private static Document MakeDocument() => new()
    {
        Id = Guid.NewGuid(),
        DocumentNumber = "ER-2026-0001",
        InvoiceNumber = "RE-1",
        SupplierId = Guid.NewGuid(),
        Status = DocumentStatus.Draft
    };

    [Fact]
    public async Task SaveChanges_NeuerBeleg_ErzeugtCreatedEintrag()
    {
        await using var context = CreateContext();
        var doc = MakeDocument();

        context.Documents.Add(doc);
        await context.SaveChangesAsync();

        var entries = await context.AuditLogEntries.ToListAsync();
        entries.Should().ContainSingle(e =>
            e.EntityName == nameof(Document) &&
            e.EntityId == doc.Id.ToString() &&
            e.Action == "Created");
    }

    [Fact]
    public async Task SaveChanges_StatusGeaendert_ErzeugtModifiedEintragMitAltUndNeu()
    {
        await using var context = CreateContext();
        var doc = MakeDocument();
        context.Documents.Add(doc);
        await context.SaveChangesAsync();

        doc.Status = DocumentStatus.Booked;
        await context.SaveChangesAsync();

        var entry = await context.AuditLogEntries
            .SingleAsync(e => e.Action == "Modified" && e.FieldName == "Status");

        entry.OldValue.Should().Be("Draft");
        entry.NewValue.Should().Be("Booked");
        entry.EntityId.Should().Be(doc.Id.ToString());
    }

    [Fact]
    public async Task SaveChanges_ZahlungErfasst_ErzeugtCreatedEintragUnterBeleg()
    {
        await using var context = CreateContext();
        var doc = MakeDocument();
        context.Documents.Add(doc);
        await context.SaveChangesAsync();

        var amount = 59.50m;
        var payment = new DocumentPayment { Id = Guid.NewGuid(), DocumentId = doc.Id, Amount = amount, PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow) };
        context.DocumentPayments.Add(payment);
        await context.SaveChangesAsync();

        var entries = await context.AuditLogEntries
            .Where(e => e.EntityName == nameof(Document) && e.EntityId == doc.Id.ToString() && e.Action == "Created" && e.FieldName == "Amount")
            .ToListAsync();

        // Formatierung folgt der aktuellen Thread-Culture (wie im echten Betrieb, wo ProgramApi
        // sie fest auf de-DE setzt) — bewusst gegen dieselbe .ToString()-Formatierung verglichen.
        entries.Should().ContainSingle(e => e.NewValue == amount.ToString());
    }

    [Fact]
    public async Task SaveChanges_UngelistetesFeldGeaendert_ErzeugtKeinenEintrag()
    {
        await using var context = CreateContext();
        var doc = MakeDocument();
        context.Documents.Add(doc);
        await context.SaveChangesAsync();
        var countAfterCreate = await context.AuditLogEntries.CountAsync();

        // AmountNet19 ist NICHT auf der Whitelist
        doc.AmountNet19 = 999;
        await context.SaveChangesAsync();

        var countAfterChange = await context.AuditLogEntries.CountAsync();
        countAfterChange.Should().Be(countAfterCreate);
    }
}
