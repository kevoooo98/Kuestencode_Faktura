using Kuestencode.Core.Enums;
using Kuestencode.Core.Models;
using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services.Pdf;
using Kuestencode.Faktura.Services.Pdf.Layouts;
using Kuestencode.Shared.ApiClients;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kuestencode.Faktura.Services;

/// <summary>
/// Coordinates PDF generation by selecting the appropriate layout renderer.
/// This is the main service that implements IPdfGeneratorService.
/// </summary>
public class PdfGeneratorService : IPdfGeneratorService
{
    private readonly FakturaDbContext _context;
    private readonly IHostApiClient _hostApiClient;
    private readonly IActaApiClient _actaApiClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PdfGeneratorService> _logger;

    public PdfGeneratorService(
        FakturaDbContext context,
        IHostApiClient hostApiClient,
        IActaApiClient actaApiClient,
        IServiceProvider serviceProvider,
        ILogger<PdfGeneratorService> logger)
    {
        _context = context;
        _hostApiClient = hostApiClient;
        _actaApiClient = actaApiClient;
        _serviceProvider = serviceProvider;
        _logger = logger;

        // QuestPDF License configuration for community use
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]> GenerateInvoicePdfAsync(int invoiceId)
    {
        _logger.LogInformation("PdfGenerator: start (InvoiceId={InvoiceId})", invoiceId);

        var invoice = _context.Invoices
            .Include(i => i.Items)
            .Include(i => i.DownPayments)
            .FirstOrDefault(i => i.Id == invoiceId);

        if (invoice == null)
        {
            throw new InvalidOperationException("Rechnung nicht gefunden");
        }

        await LoadSourceInvoicesAsync(invoice);

        // Lade Customer und Company via Host API
        _logger.LogInformation("PdfGenerator: loading customer (InvoiceId={InvoiceId}, CustomerId={CustomerId})", invoiceId, invoice.CustomerId);
        var customerDto = await _hostApiClient.GetCustomerAsync(invoice.CustomerId);
        if (customerDto != null)
        {
            invoice.Customer = new Customer
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

        if (invoice.ProjectId.HasValue)
        {
            try
            {
                var project = await _actaApiClient.GetProjectByExternalIdAsync(invoice.ProjectId.Value);
                invoice.ProjectName = project?.Name;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PdfGenerator: Projektname konnte nicht geladen werden (InvoiceId={InvoiceId}, ProjectId={ProjectId})", invoiceId, invoice.ProjectId);
            }
        }

        _logger.LogInformation("PdfGenerator: loading company (InvoiceId={InvoiceId})", invoiceId);
        var companyDto = await _hostApiClient.GetCompanyAsync();
        if (companyDto == null)
        {
            throw new InvalidOperationException("Firmendaten nicht gefunden");
        }

        var company = new Company
        {
            Id = companyDto.Id,
            OwnerFullName = companyDto.OwnerFullName,
            BusinessName = companyDto.BusinessName,
            Address = companyDto.Address,
            PostalCode = companyDto.PostalCode,
            City = companyDto.City,
            Country = companyDto.Country,
            TaxNumber = companyDto.TaxNumber,
            VatId = companyDto.VatId,
            IsKleinunternehmer = companyDto.IsKleinunternehmer,
            BankName = companyDto.BankName,
            BankAccount = companyDto.BankAccount,
            Bic = companyDto.Bic,
            AccountHolder = companyDto.AccountHolder,
            Email = companyDto.Email,
            Phone = companyDto.Phone,
            Website = companyDto.Website,
            LogoData = companyDto.LogoData,
            LogoContentType = companyDto.LogoContentType,
            PdfLayout = Enum.Parse<PdfLayout>(companyDto.PdfLayout),
            PdfPrimaryColor = companyDto.PdfPrimaryColor,
            PdfAccentColor = companyDto.PdfAccentColor,
            PdfHeaderText = companyDto.PdfHeaderText,
            PdfFooterText = companyDto.PdfFooterText,
            PdfPaymentNotice = companyDto.PdfPaymentNotice,
            PdfCreditNoteHeaderText = companyDto.PdfCreditNoteHeaderText,
            PdfCreditNoteFooterText = companyDto.PdfCreditNoteFooterText,
            PdfCreditNotePaymentNotice = companyDto.PdfCreditNotePaymentNotice,
            AdditionalBankAccounts = companyDto.AdditionalBankAccounts
                .Select(a => new AdditionalBankAccount
                {
                    BankName = a.BankName,
                    Iban = a.Iban,
                    Bic = a.Bic,
                    AccountHolder = a.AccountHolder,
                    SortOrder = a.SortOrder
                }).ToList()
        };

        _logger.LogInformation("PdfGenerator: rendering pdf (InvoiceId={InvoiceId})", invoiceId);
        return GeneratePdfWithCompany(invoice, company);
    }

    public byte[] GenerateInvoicePdf(int invoiceId)
    {
        return GenerateInvoicePdfAsync(invoiceId).GetAwaiter().GetResult();
    }

    public byte[] GeneratePdfWithCompany(Invoice invoice, Company company)
    {
        // Select the appropriate layout renderer based on company settings
        var layoutRenderer = GetLayoutRenderer(company.PdfLayout);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor("#1A1A1A"));

                page.Header().Element(c => layoutRenderer.RenderHeader(c, invoice, company));
                page.Content().Element(c => layoutRenderer.RenderContent(c, invoice, company));
                page.Footer().Element(c => layoutRenderer.RenderFooter(c, company));
            });
        });

        var pdfBytes = document.GeneratePdf();
        _logger.LogInformation(
            "PdfGenerator: done (InvoiceId={InvoiceId}, Size={Size}, Ms={Ms})",
            invoice.Id,
            pdfBytes.Length,
            sw.ElapsedMilliseconds);
        return pdfBytes;
    }

    public async Task FreezeSnapshotAsync(int invoiceId)
    {
        var alreadyFrozen = await _context.InvoiceAttachments
            .AnyAsync(a => a.InvoiceId == invoiceId && a.IsFrozenSnapshot);
        if (alreadyFrozen)
        {
            return;
        }

        var invoice = await _context.Invoices.FindAsync(invoiceId);
        if (invoice == null)
        {
            throw new InvalidOperationException("Rechnung nicht gefunden");
        }

        var pdfBytes = await GenerateInvoicePdfAsync(invoiceId);

        _context.InvoiceAttachments.Add(new InvoiceAttachment
        {
            InvoiceId = invoiceId,
            FileName = $"{invoice.InvoiceNumber}.pdf",
            ContentType = "application/pdf",
            FileSize = pdfBytes.Length,
            Data = pdfBytes,
            IsFrozenSnapshot = true
        });

        await _context.SaveChangesAsync();
        _logger.LogInformation("PdfGenerator: Snapshot eingefroren (InvoiceId={InvoiceId})", invoiceId);
    }

    public async Task<byte[]?> TryGetFrozenSnapshotAsync(int invoiceId)
    {
        var snapshot = await _context.InvoiceAttachments
            .AsNoTracking()
            .Where(a => a.InvoiceId == invoiceId && a.IsFrozenSnapshot)
            .Select(a => a.Data)
            .FirstOrDefaultAsync();

        return snapshot;
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

        var sourceInvoices = await _context.Invoices
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

    /// <summary>
    /// Factory method to get the appropriate layout renderer based on layout type.
    /// </summary>
    private IPdfLayoutRenderer GetLayoutRenderer(PdfLayout layout)
    {
        return layout switch
        {
            PdfLayout.Klar => _serviceProvider.GetRequiredService<KlarLayoutRenderer>(),
            PdfLayout.Strukturiert => _serviceProvider.GetRequiredService<StrukturiertLayoutRenderer>(),
            PdfLayout.Betont => _serviceProvider.GetRequiredService<BetontLayoutRenderer>(),
            _ => _serviceProvider.GetRequiredService<KlarLayoutRenderer>() // Fallback
        };
    }
}
