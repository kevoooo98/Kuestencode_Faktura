using Kuestencode.Core.Auditing;
using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Data.Repositories;
using Kuestencode.Faktura.Models;
using Kuestencode.Shared.Contracts.Faktura;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Faktura.Services;

public interface IInvoiceService
{
    Task<List<Invoice>> GetAllAsync();
    Task<List<Invoice>> GetByStatusAsync(InvoiceStatus status);
    Task<List<Invoice>> GetByTypeAsync(InvoiceType type);
    Task<List<Invoice>> GetByProjectIdAsync(int projectId);
    Task<List<Invoice>> GetPaidByDateRangeAsync(DateTime paidFrom, DateTime paidTo);
    Task<Invoice?> GetByIdAsync(int id, bool includeCustomer = true, bool includeItems = true);
    Task<Invoice> CreateAsync(Invoice invoice);
    Task<Invoice> CreateCreditNoteFromInvoiceAsync(int sourceInvoiceId, DateTime creditNoteDate);
    Task UpdateAsync(Invoice invoice);
    Task DeleteAsync(int id);
    Task CancelAsync(int id, string? reason);
    Task<List<AuditLogEntryDto>> GetAuditLogAsync(int id);
    Task<AuditHashChain.ChainVerificationResult> VerifyAuditLogChainAsync();
    Task<string> GenerateInvoiceNumberAsync();
    Task<(string Prefix, string Suffix, int SequenceLength)> GetInvoiceNumberFormatPartsAsync();
    Task<string> GenerateCreditNoteNumberAsync();
    Task<(string Prefix, string Suffix, int SequenceLength)> GetCreditNoteNumberFormatPartsAsync();
    Task MarkAsPaidAsync(int id, DateTime paidDate);
    Task MarkAsPrintedAsync(int id);
    Task<decimal> CalculateTotalNetAsync(List<InvoiceItem> items);
    Task<decimal> CalculateTotalGrossAsync(List<InvoiceItem> items, bool isKleinunternehmer);
}

public class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoicePaymentService _paymentService;
    private readonly IPdfGeneratorService _pdfGeneratorService;
    private readonly FakturaDbContext _context;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(
        IInvoiceRepository invoiceRepository,
        IInvoicePaymentService paymentService,
        IPdfGeneratorService pdfGeneratorService,
        FakturaDbContext context,
        ILogger<InvoiceService> logger)
    {
        _invoiceRepository = invoiceRepository;
        _paymentService = paymentService;
        _pdfGeneratorService = pdfGeneratorService;
        _context = context;
        _logger = logger;
    }

    public async Task<List<Invoice>> GetAllAsync()
    {
        try
        {
            var invoices = await _invoiceRepository.GetAllAsync();
            return invoices.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen aller Rechnungen");
            throw;
        }
    }

    public async Task<List<Invoice>> GetByStatusAsync(InvoiceStatus status)
    {
        try
        {
            var invoices = await _invoiceRepository.GetByStatusAsync(status);
            return invoices.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Rechnungen mit Status {Status}", status);
            throw;
        }
    }

    public async Task<List<Invoice>> GetByTypeAsync(InvoiceType type)
    {
        try
        {
            var invoices = await _invoiceRepository.GetByTypeAsync(type);
            return invoices.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Rechnungen mit Typ {Type}", type);
            throw;
        }
    }

    public async Task<List<Invoice>> GetByProjectIdAsync(int projectId)
    {
        try
        {
            var invoices = await _invoiceRepository.GetByProjectIdAsync(projectId);
            return invoices.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Rechnungen für Projekt {ProjectId}", projectId);
            throw;
        }
    }

    public async Task<List<Invoice>> GetPaidByDateRangeAsync(DateTime paidFrom, DateTime paidTo)
    {
        try
        {
            var invoices = await _invoiceRepository.GetPaidByDateRangeAsync(paidFrom, paidTo);
            return invoices.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen bezahlter Rechnungen für {From} - {To}", paidFrom, paidTo);
            throw;
        }
    }

    public async Task<Invoice?> GetByIdAsync(int id, bool includeCustomer = true, bool includeItems = true)
    {
        try
        {
            if (includeCustomer || includeItems)
            {
                return await _invoiceRepository.GetWithDetailsAsync(id);
            }
            return await _invoiceRepository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen der Rechnung mit ID {InvoiceId}", id);
            throw;
        }
    }

    public async Task<Invoice> CreateAsync(Invoice invoice)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
            {
                invoice.InvoiceNumber = await GenerateInvoiceNumberAsync();
            }

            var exists = await _invoiceRepository.InvoiceNumberExistsAsync(invoice.InvoiceNumber);
            if (exists)
            {
                throw new InvalidOperationException($"Rechnungsnummer {invoice.InvoiceNumber} existiert bereits.");
            }

            // Set position numbers
            for (int i = 0; i < invoice.Items.Count; i++)
            {
                invoice.Items[i].Position = i + 1;
            }

            return await _invoiceRepository.AddAsync(invoice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Erstellen der Rechnung");
            throw;
        }
    }

    public async Task<Invoice> CreateCreditNoteFromInvoiceAsync(int sourceInvoiceId, DateTime creditNoteDate)
    {
        try
        {
            var sourceInvoice = await _invoiceRepository.GetByIdAsync(sourceInvoiceId)
                ?? throw new InvalidOperationException($"Rechnung mit ID {sourceInvoiceId} wurde nicht gefunden.");

            var creditNote = new Invoice
            {
                Type = InvoiceType.CreditNote,
                RelatedInvoiceId = sourceInvoiceId,
                InvoiceNumber = await GenerateCreditNoteNumberAsync(),
                InvoiceDate = creditNoteDate.Kind == DateTimeKind.Utc ? creditNoteDate : DateTime.SpecifyKind(creditNoteDate.Date, DateTimeKind.Utc),
                CustomerId = sourceInvoice.CustomerId,
                ProjectId = sourceInvoice.ProjectId,
                Status = InvoiceStatus.Draft,
                Items = sourceInvoice.Items.Select(item => new InvoiceItem
                {
                    Description = item.Description,
                    Unit = item.Unit,
                    Quantity = item.Quantity,
                    UnitPrice = -Math.Abs(item.UnitPrice),
                    VatRate = item.VatRate,
                    IsHeader = item.IsHeader
                }).ToList()
            };

            return await CreateAsync(creditNote);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Erstellen der Gutschrift aus Rechnung {InvoiceId}", sourceInvoiceId);
            throw;
        }
    }

    public async Task UpdateAsync(Invoice invoice)
    {
        try
        {
            var existingInvoice = await _invoiceRepository.GetByIdAsync(invoice.Id);
            if (existingInvoice == null)
            {
                throw new InvalidOperationException($"Rechnung mit ID {invoice.Id} wurde nicht gefunden.");
            }

            if (existingInvoice.IsLocked)
            {
                throw new InvalidOperationException(
                    $"Rechnung {existingInvoice.InvoiceNumber} kann nicht mehr bearbeitet werden (Status: {existingInvoice.Status}). Korrektur nur über Gutschrift oder Storno.");
            }

            // Update position numbers
            for (int i = 0; i < invoice.Items.Count; i++)
            {
                invoice.Items[i].Position = i + 1;
            }

            await _invoiceRepository.UpdateAsync(invoice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Aktualisieren der Rechnung mit ID {InvoiceId}", invoice.Id);
            throw;
        }
    }

    public async Task DeleteAsync(int id)
    {
        try
        {
            var invoice = await _invoiceRepository.GetByIdAsync(id);
            if (invoice == null)
            {
                throw new InvalidOperationException($"Rechnung mit ID {id} wurde nicht gefunden.");
            }

            if (invoice.IsLocked)
            {
                throw new InvalidOperationException(
                    $"Rechnung {invoice.InvoiceNumber} kann nicht gelöscht werden (Status: {invoice.Status}). Nummer bleibt aus GoBD-Gründen erhalten — Korrektur über Gutschrift oder Storno.");
            }

            await _invoiceRepository.DeleteAsync(invoice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Löschen der Rechnung mit ID {InvoiceId}", id);
            throw;
        }
    }

    public async Task CancelAsync(int id, string? reason)
    {
        try
        {
            var invoice = await _invoiceRepository.GetByIdAsync(id);
            if (invoice == null)
            {
                throw new InvalidOperationException($"Rechnung mit ID {id} wurde nicht gefunden.");
            }

            if (invoice.Status == InvoiceStatus.Draft)
            {
                throw new InvalidOperationException("Entwürfe werden gelöscht, nicht storniert.");
            }

            if (invoice.Status == InvoiceStatus.Cancelled)
            {
                throw new InvalidOperationException($"Rechnung {invoice.InvoiceNumber} ist bereits storniert.");
            }

            invoice.Status = InvoiceStatus.Cancelled;
            invoice.CancelledAt = DateTime.UtcNow;
            invoice.CancellationReason = reason;

            await _invoiceRepository.UpdateAsync(invoice);
            _logger.LogInformation("Rechnung {InvoiceNumber} storniert", invoice.InvoiceNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Stornieren der Rechnung mit ID {InvoiceId}", id);
            throw;
        }
    }

    public async Task<string> GenerateInvoiceNumberAsync()
    {
        try
        {
            return await _invoiceRepository.GenerateInvoiceNumberAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Generieren der Rechnungsnummer");
            throw;
        }
    }

    public async Task<(string Prefix, string Suffix, int SequenceLength)> GetInvoiceNumberFormatPartsAsync()
    {
        try
        {
            return await _invoiceRepository.GetInvoiceNumberFormatPartsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Ermitteln des Rechnungsnummer-Formats");
            throw;
        }
    }

    public async Task<string> GenerateCreditNoteNumberAsync()
    {
        try
        {
            return await _invoiceRepository.GenerateCreditNoteNumberAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Generieren der Gutschriftnummer");
            throw;
        }
    }

    public async Task<(string Prefix, string Suffix, int SequenceLength)> GetCreditNoteNumberFormatPartsAsync()
    {
        try
        {
            return await _invoiceRepository.GetCreditNoteNumberFormatPartsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Ermitteln des Gutschriftnummer-Formats");
            throw;
        }
    }

    public async Task MarkAsPaidAsync(int id, DateTime paidDate)
    {
        try
        {
            var invoice = await _invoiceRepository.GetByIdAsync(id);
            if (invoice == null)
            {
                throw new InvalidOperationException($"Rechnung mit ID {id} wurde nicht gefunden.");
            }

            var utcPaidDate = paidDate.Kind == DateTimeKind.Utc ? paidDate : paidDate.ToUniversalTime();

            if (invoice.RemainingAmount != 0)
            {
                await _paymentService.ZahlungErfassenAsync(id, invoice.RemainingAmount, utcPaidDate, "Als beglichen markiert");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Markieren der Rechnung als bezahlt (ID: {InvoiceId})", id);
            throw;
        }
    }

    public async Task MarkAsPrintedAsync(int id)
    {
        try
        {
            var invoice = await _invoiceRepository.GetByIdAsync(id);
            if (invoice == null)
            {
                throw new InvalidOperationException($"Rechnung mit ID {id} wurde nicht gefunden.");
            }

            invoice.PrintedAt = DateTime.UtcNow;
            invoice.PrintCount++;

            // If status is Draft, change to Sent
            if (invoice.Status == InvoiceStatus.Draft)
            {
                invoice.Status = InvoiceStatus.Sent;
            }

            await _invoiceRepository.UpdateAsync(invoice);

            // Rechnung ab dem ersten Verlassen des Systems (Druck) unveränderlich einfrieren (GoBD)
            await _pdfGeneratorService.FreezeSnapshotAsync(id);

            _logger.LogInformation("Rechnung {InvoiceNumber} als gedruckt markiert (Druckzähler: {PrintCount})",
                invoice.InvoiceNumber, invoice.PrintCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Markieren der Rechnung als gedruckt (ID: {InvoiceId})", id);
            throw;
        }
    }

    public Task<decimal> CalculateTotalNetAsync(List<InvoiceItem> items)
    {
        var total = items.Sum(item => item.TotalNet);
        return Task.FromResult(total);
    }

    public async Task<List<AuditLogEntryDto>> GetAuditLogAsync(int id)
    {
        var entityId = id.ToString();
        return await _context.AuditLogEntries
            .Where(a => a.EntityName == nameof(Invoice) && a.EntityId == entityId)
            .OrderByDescending(a => a.ChangedAt)
            .Select(a => new AuditLogEntryDto
            {
                Action = a.Action,
                FieldName = a.FieldName,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                ChangedByUserName = a.ChangedByUserName,
                ChangedAt = a.ChangedAt
            })
            .ToListAsync();
    }

    public async Task<AuditHashChain.ChainVerificationResult> VerifyAuditLogChainAsync()
    {
        var entries = await _context.AuditLogEntries
            .OrderBy(a => a.SequenceNumber)
            .ToListAsync();

        return AuditHashChain.VerifyChain(entries);
    }

    public Task<decimal> CalculateTotalGrossAsync(List<InvoiceItem> items, bool isKleinunternehmer)
    {
        if (isKleinunternehmer)
        {
            // Kleinunternehmer: keine MwSt
            var total = items.Sum(item => item.TotalNet);
            return Task.FromResult(total);
        }
        else
        {
            var total = items.Sum(item => item.TotalGross);
            return Task.FromResult(total);
        }
    }
}
