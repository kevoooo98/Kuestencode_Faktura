using Kuestencode.Core.Services;
using Kuestencode.Faktura.Models;
using Kuestencode.Shared.ApiClients;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Faktura.Data.Repositories;

public class InvoiceRepository : Repository<Invoice>, IInvoiceRepository
{
    private readonly IHostApiClient _hostApiClient;

    public InvoiceRepository(
        FakturaDbContext context,
        IHostApiClient hostApiClient) : base(context)
    {
        _hostApiClient = hostApiClient;
    }

    public async Task<Invoice?> GetByInvoiceNumberAsync(string invoiceNumber)
    {
        var invoice = await _dbSet
            .Include(i => i.Items)
            .Include(i => i.DownPayments)
            .Include(i => i.Attachments)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber);

        if (invoice != null)
        {
            await LoadCustomerAsync(invoice);
        }

        return invoice;
    }

    public async Task<bool> InvoiceNumberExistsAsync(string invoiceNumber)
    {
        return await _dbSet
            .AnyAsync(i => i.InvoiceNumber == invoiceNumber);
    }

    public async Task<string> GenerateInvoiceNumberAsync()
    {
        return await GenerateNumberAsync(await GetInvoiceNumberFormatAsync(), InvoiceType.Invoice);
    }

    public async Task<(string Prefix, string Suffix, int SequenceLength)> GetInvoiceNumberFormatPartsAsync()
    {
        var format = await GetInvoiceNumberFormatAsync();
        return DocumentNumberFormatter.SplitAroundSequence(format, DateTime.Now);
    }

    public async Task<string> GenerateCreditNoteNumberAsync()
    {
        return await GenerateNumberAsync(await GetCreditNoteNumberFormatAsync(), InvoiceType.CreditNote);
    }

    public async Task<(string Prefix, string Suffix, int SequenceLength)> GetCreditNoteNumberFormatPartsAsync()
    {
        var format = await GetCreditNoteNumberFormatAsync();
        return DocumentNumberFormatter.SplitAroundSequence(format, DateTime.Now);
    }

    private async Task<string> GenerateNumberAsync(string format, InvoiceType type)
    {
        var referenceDate = DateTime.Now;
        var prefix = DocumentNumberFormatter.SplitAroundSequence(format, referenceDate).Prefix;
        var sequenceKey = $"{type}:{prefix}";

        return await NumberSequenceService.GetNextNumberAsync(
            _context.Database,
            "faktura.\"NumberSequences\"",
            sequenceKey,
            format,
            referenceDate,
            seedFromExistingAsync: async () =>
            {
                var existingNumbers = await _dbSet
                    .Where(i => i.Type == type)
                    .Select(i => i.InvoiceNumber)
                    .ToListAsync();

                return DocumentNumberFormatter.GetLastSequenceNumber(format, referenceDate, existingNumbers);
            });
    }

    private async Task<string> GetInvoiceNumberFormatAsync()
    {
        var settings = await _hostApiClient.GetNumberFormatSettingsAsync();
        return !string.IsNullOrWhiteSpace(settings?.InvoiceFormat)
            ? settings.InvoiceFormat.Trim()
            : "YYYY-XXXX";
    }

    private async Task<string> GetCreditNoteNumberFormatAsync()
    {
        var settings = await _hostApiClient.GetNumberFormatSettingsAsync();
        return !string.IsNullOrWhiteSpace(settings?.CreditNoteFormat)
            ? settings.CreditNoteFormat.Trim()
            : "GS-YYYY-XXXX";
    }

    public async Task<IEnumerable<Invoice>> GetByCustomerIdAsync(int customerId)
    {
        var invoices = await _dbSet
            .Include(i => i.Items)
            .Include(i => i.DownPayments)
            .Include(i => i.Attachments)
            .Include(i => i.Payments)
            .Where(i => i.CustomerId == customerId)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        await LoadCustomersAsync(invoices);
        return invoices;
    }

    public async Task<IEnumerable<Invoice>> GetByStatusAsync(InvoiceStatus status)
    {
        var invoices = await _dbSet
            .Include(i => i.Items)
            .Include(i => i.DownPayments)
            .Include(i => i.Attachments)
            .Include(i => i.Payments)
            .Where(i => i.Status == status)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        await LoadCustomersAsync(invoices);
        return invoices;
    }

    public async Task<IEnumerable<Invoice>> GetByTypeAsync(InvoiceType type)
    {
        var invoices = await _dbSet
            .Include(i => i.Items)
            .Include(i => i.DownPayments)
            .Include(i => i.Attachments)
            .Include(i => i.Payments)
            .Where(i => i.Type == type)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        await LoadCustomersAsync(invoices);
        return invoices;
    }

    public async Task<IEnumerable<Invoice>> GetPaidByDateRangeAsync(DateTime paidFrom, DateTime paidTo)
    {
        var from = DateTime.SpecifyKind(paidFrom, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(paidTo, DateTimeKind.Utc);
        var invoices = await _dbSet
            .Include(i => i.Items)
            .Include(i => i.DownPayments)
            .Include(i => i.Attachments)
            .Include(i => i.Payments)
            .Where(i => i.Status == InvoiceStatus.Paid &&
                       i.PaidDate.HasValue &&
                       i.PaidDate.Value >= from &&
                       i.PaidDate.Value <= to)
            .OrderBy(i => i.PaidDate)
            .ToListAsync();

        await LoadCustomersAsync(invoices);
        return invoices;
    }

    public async Task<IEnumerable<Invoice>> GetOverdueInvoicesAsync()
    {
        var today = DateTime.Today;

        var invoices = await _dbSet
            .Include(i => i.Items)
            .Include(i => i.DownPayments)
            .Include(i => i.Attachments)
            .Include(i => i.Payments)
            .Where(i => i.Status == InvoiceStatus.Sent &&
                       i.DueDate.HasValue &&
                       i.DueDate.Value < today)
            .OrderBy(i => i.DueDate)
            .ToListAsync();

        await LoadCustomersAsync(invoices);
        return invoices;
    }

    public async Task<Invoice?> GetWithDetailsAsync(int id)
    {
        var invoice = await _dbSet
            .Include(i => i.Items.OrderBy(item => item.Position))
            .Include(i => i.DownPayments)
            .Include(i => i.Attachments)
            .Include(i => i.Payments.OrderByDescending(p => p.PaymentDate))
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice != null)
        {
            await LoadCustomerAsync(invoice);
            await LoadSourceInvoicesAsync(invoice);
        }

        return invoice;
    }

    /// <summary>
    /// Lädt für jeden Abschlag die verknüpfte Abschlagsrechnung inkl. Positionen und Zahlungen nach,
    /// da SourceInvoice als [NotMapped]-Navigation nicht per Include geladen werden kann.
    /// </summary>
    private async Task LoadSourceInvoicesAsync(Invoice invoice)
    {
        var sourceInvoiceIds = invoice.DownPayments
            .Where(d => d.SourceInvoiceId.HasValue)
            .Select(d => d.SourceInvoiceId!.Value)
            .Distinct()
            .ToList();

        if (sourceInvoiceIds.Count == 0)
            return;

        var sourceInvoices = await _dbSet
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .Where(i => sourceInvoiceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id);

        foreach (var downPayment in invoice.DownPayments.Where(d => d.SourceInvoiceId.HasValue))
        {
            if (sourceInvoices.TryGetValue(downPayment.SourceInvoiceId!.Value, out var sourceInvoice))
            {
                downPayment.SourceInvoice = sourceInvoice;
            }
        }
    }

    public override async Task<IEnumerable<Invoice>> GetAllAsync()
    {
        var invoices = await _dbSet
            .Include(i => i.Items)
            .Include(i => i.DownPayments)
            .Include(i => i.Attachments)
            .Include(i => i.Payments)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        await LoadCustomersAsync(invoices);
        return invoices;
    }

    public override async Task<Invoice?> GetByIdAsync(int id)
    {
        return await GetWithDetailsAsync(id);
    }

    public async Task<IEnumerable<Invoice>> GetByProjectIdAsync(int projectId)
    {
        var invoices = await _dbSet
            .Include(i => i.Items)
            .Include(i => i.DownPayments)
            .Include(i => i.Payments)
            .Where(i => i.ProjectId == projectId)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync();

        await LoadCustomersAsync(invoices);
        return invoices;
    }

    /// <summary>
    /// Lädt den Kunden für eine einzelne Rechnung via Host API.
    /// </summary>
    private async Task LoadCustomerAsync(Invoice invoice)
    {
        var customerDto = await _hostApiClient.GetCustomerAsync(invoice.CustomerId);
        if (customerDto != null)
        {
            invoice.Customer = new Core.Models.Customer
            {
                Id = customerDto.Id,
                CustomerNumber = customerDto.CustomerNumber,
                Name = customerDto.Name,
                Address = customerDto.Address,
                PostalCode = customerDto.PostalCode,
                City = customerDto.City,
                Country = customerDto.Country,
                Email = customerDto.Email,
                Phone = customerDto.Phone,
                Notes = customerDto.Notes,
                Salutation = customerDto.Salutation
            };
        }
    }

    /// <summary>
    /// Lädt Kunden für mehrere Rechnungen effizient via Host API.
    /// </summary>
    private async Task LoadCustomersAsync(IEnumerable<Invoice> invoices)
    {
        var customerIds = invoices.Select(i => i.CustomerId).Distinct().ToList();

        // Alle Kunden via Host API laden
        var allCustomerDtos = await _hostApiClient.GetAllCustomersAsync();
        var customerDict = allCustomerDtos
            .Where(c => customerIds.Contains(c.Id))
            .ToDictionary(c => c.Id, c => new Core.Models.Customer
            {
                Id = c.Id,
                CustomerNumber = c.CustomerNumber,
                Name = c.Name,
                Address = c.Address,
                PostalCode = c.PostalCode,
                City = c.City,
                Country = c.Country,
                Email = c.Email,
                Phone = c.Phone,
                Notes = c.Notes,
                Salutation = c.Salutation
            });

        foreach (var invoice in invoices)
        {
            if (customerDict.TryGetValue(invoice.CustomerId, out var customer))
            {
                invoice.Customer = customer;
            }
        }
    }
}
