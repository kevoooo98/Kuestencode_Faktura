using InvoiceApp.Data.Repositories;
using InvoiceApp.Models;

namespace InvoiceApp.Services;

public interface IInvoiceService
{
    Task<List<Invoice>> GetAllAsync();
    Task<List<Invoice>> GetByStatusAsync(InvoiceStatus status);
    Task<Invoice?> GetByIdAsync(int id, bool includeCustomer = true, bool includeItems = true);
    Task<Invoice> CreateAsync(Invoice invoice);
    Task UpdateAsync(Invoice invoice);
    Task DeleteAsync(int id);
    Task<string> GenerateInvoiceNumberAsync();
    Task MarkAsPaidAsync(int id);
    Task MarkAsPrintedAsync(int id);
    Task<decimal> CalculateTotalNetAsync(List<InvoiceItem> items);
    Task<decimal> CalculateTotalGrossAsync(List<InvoiceItem> items, bool isKleinunternehmer);
}

public class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ILogger<InvoiceService> _logger;

    public InvoiceService(IInvoiceRepository invoiceRepository, ILogger<InvoiceService> logger)
    {
        _invoiceRepository = invoiceRepository;
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

    public async Task UpdateAsync(Invoice invoice)
    {
        try
        {
            var existingInvoice = await _invoiceRepository.GetByIdAsync(invoice.Id);
            if (existingInvoice == null)
            {
                throw new InvalidOperationException($"Rechnung mit ID {invoice.Id} wurde nicht gefunden.");
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

            await _invoiceRepository.DeleteAsync(invoice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Löschen der Rechnung mit ID {InvoiceId}", id);
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

    public async Task MarkAsPaidAsync(int id)
    {
        try
        {
            var invoice = await _invoiceRepository.GetByIdAsync(id);
            if (invoice == null)
            {
                throw new InvalidOperationException($"Rechnung mit ID {id} wurde nicht gefunden.");
            }

            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidDate = DateTime.UtcNow;

            await _invoiceRepository.UpdateAsync(invoice);
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
