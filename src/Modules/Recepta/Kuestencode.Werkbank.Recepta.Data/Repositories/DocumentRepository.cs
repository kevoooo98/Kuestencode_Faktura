using Kuestencode.Core.Services;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Werkbank.Recepta.Domain.Entities;
using Kuestencode.Werkbank.Recepta.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Werkbank.Recepta.Data.Repositories;

/// <summary>
/// Repository-Implementierung für Belege.
/// </summary>
public class DocumentRepository : IDocumentRepository
{
    private readonly ReceptaDbContext _context;
    private readonly IHostApiClient _hostApiClient;

    public DocumentRepository(ReceptaDbContext context, IHostApiClient hostApiClient)
    {
        _context = context;
        _hostApiClient = hostApiClient;
    }

    public async Task<Document?> GetByIdAsync(Guid id)
    {
        return await _context.Documents
            .Include(d => d.Supplier)
            .Include(d => d.Files)
            .Include(d => d.ProjectAllocations)
            .Include(d => d.Payments.OrderByDescending(p => p.PaymentDate))
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<List<Document>> GetAllAsync(
        DocumentStatus? status = null,
        DocumentCategory? category = null,
        Guid? supplierId = null,
        bool? hasBeenAttached = null)
    {
        var query = _context.Documents
            .Include(d => d.Supplier)
            .Include(d => d.Files)
            .Include(d => d.ProjectAllocations)
            .Include(d => d.Payments)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(d => d.Status == status.Value);
        }

        if (category.HasValue)
        {
            query = query.Where(d => d.Category == category.Value);
        }

        if (supplierId.HasValue)
        {
            query = query.Where(d => d.SupplierId == supplierId.Value);
        }

        if (hasBeenAttached.HasValue)
        {
            query = query.Where(d => d.HasBeenAttached == hasBeenAttached.Value);
        }

        return await query
            .OrderByDescending(d => d.InvoiceDate)
            .ThenByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task AddAsync(Document document)
    {
        await _context.Documents.AddAsync(document);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Document document)
    {
        _context.Documents.Update(document);
        await _context.SaveChangesAsync();
    }

    public async Task MarkAsAttachedAsync(IEnumerable<Guid> documentIds)
    {
        var ids = documentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var documents = await _context.Documents
            .Where(d => ids.Contains(d.Id))
            .ToListAsync();

        foreach (var document in documents)
        {
            document.HasBeenAttached = true;
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var document = await _context.Documents.FindAsync(id);
        if (document == null)
        {
            throw new InvalidOperationException($"Beleg mit ID {id} nicht gefunden.");
        }

        if (document.Status != DocumentStatus.Draft)
        {
            throw new InvalidOperationException(
                "Beleg kann nicht gelöscht werden. Nur Belege im Status 'Draft' können gelöscht werden.");
        }

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsNumberAsync(string documentNumber)
    {
        return await _context.Documents.AnyAsync(d => d.DocumentNumber == documentNumber);
    }

    public async Task<string> GenerateDocumentNumberAsync()
    {
        var settings = await _hostApiClient.GetNumberFormatSettingsAsync();
        var format = !string.IsNullOrWhiteSpace(settings?.IncomingInvoiceFormat)
            ? settings.IncomingInvoiceFormat.Trim()
            : "ER-YYYY-XXXX";

        var referenceDate = DateTime.Now;
        var sequenceKey = DocumentNumberFormatter.SplitAroundSequence(format, referenceDate).Prefix;

        return await NumberSequenceService.GetNextNumberAsync(
            _context.Database,
            "recepta.\"NumberSequences\"",
            sequenceKey,
            format,
            referenceDate,
            seedFromExistingAsync: async () =>
            {
                var existingNumbers = await _context.Documents
                    .Select(d => d.DocumentNumber)
                    .ToListAsync();

                return DocumentNumberFormatter.GetLastSequenceNumber(format, referenceDate, existingNumbers);
            });
    }
}
