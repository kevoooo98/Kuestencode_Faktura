using Kuestencode.Core.Interfaces;
using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Kuestencode.Faktura.Services.Email;

/// <summary>
/// Builds email attachments for different invoice formats
/// </summary>
public class EmailAttachmentBuilder : IEmailAttachmentBuilder
{
    private readonly IPdfGeneratorService _pdfGenerator;
    private readonly IXRechnungService _xRechnungService;
    private readonly FakturaDbContext _dbContext;
    private readonly ILogger<EmailAttachmentBuilder> _logger;

    public EmailAttachmentBuilder(
        IPdfGeneratorService pdfGenerator,
        IXRechnungService xRechnungService,
        FakturaDbContext dbContext,
        ILogger<EmailAttachmentBuilder> logger)
    {
        _pdfGenerator = pdfGenerator;
        _xRechnungService = xRechnungService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<EmailAttachment>> BuildInvoiceAttachmentsAsync(
        int invoiceId,
        string invoiceNumber,
        EmailAttachmentFormat format)
    {
        _logger.LogInformation(
            "EmailAttachmentBuilder: start (InvoiceId={InvoiceId}, Format={Format})",
            invoiceId,
            format);

        var attachments = new List<EmailAttachment>();

        switch (format)
        {
            case EmailAttachmentFormat.NormalPdf:
                attachments.Add(await BuildNormalPdfAsync(invoiceId, invoiceNumber));
                break;

            case EmailAttachmentFormat.ZugferdPdf:
                attachments.Add(await BuildZugferdPdfAsync(invoiceId, invoiceNumber));
                break;

            case EmailAttachmentFormat.XRechnungXmlOnly:
                attachments.Add(await BuildXRechnungXmlAsync(invoiceId, invoiceNumber));
                break;

            case EmailAttachmentFormat.XRechnungXmlAndPdf:
                attachments.Add(await BuildXRechnungXmlAsync(invoiceId, invoiceNumber));
                attachments.Add(await BuildNormalPdfAsync(invoiceId, invoiceNumber));
                break;

            default:
                throw new ArgumentException($"Unsupported format: {format}");
        }

        _logger.LogInformation("EmailAttachmentBuilder: base attachments done (InvoiceId={InvoiceId})", invoiceId);
        attachments.AddRange(await BuildCustomAttachmentsAsync(invoiceId));
        _logger.LogInformation("EmailAttachmentBuilder: custom attachments done (InvoiceId={InvoiceId})", invoiceId);

        return attachments;
    }

    private async Task<EmailAttachment> BuildNormalPdfAsync(int invoiceId, string invoiceNumber)
    {
        var sw = Stopwatch.StartNew();
        var pdfBytes = await _pdfGenerator.TryGetFrozenSnapshotAsync(invoiceId);
        if (pdfBytes != null)
        {
            _logger.LogInformation("EmailAttachmentBuilder: using frozen snapshot (InvoiceId={InvoiceId})", invoiceId);
            return new EmailAttachment
            {
                FileName = $"Rechnung_{invoiceNumber}.pdf",
                Content = pdfBytes,
                ContentType = "application/pdf"
            };
        }

        _logger.LogInformation("EmailAttachmentBuilder: generating PDF (InvoiceId={InvoiceId})", invoiceId);
        pdfBytes = await _pdfGenerator.GenerateInvoicePdfAsync(invoiceId);
        _logger.LogInformation(
            "EmailAttachmentBuilder: PDF ready (InvoiceId={InvoiceId}, Size={Size}, Ms={Ms})",
            invoiceId,
            pdfBytes.Length,
            sw.ElapsedMilliseconds);

        return new EmailAttachment
        {
            FileName = $"Rechnung_{invoiceNumber}.pdf",
            Content = pdfBytes,
            ContentType = "application/pdf"
        };
    }

    private async Task<EmailAttachment> BuildZugferdPdfAsync(int invoiceId, string invoiceNumber)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("EmailAttachmentBuilder: generating ZUGFeRD PDF (InvoiceId={InvoiceId})", invoiceId);
        var zugferdPdf = await _xRechnungService.GenerateZugferdPdfAsync(invoiceId);
        _logger.LogInformation(
            "EmailAttachmentBuilder: ZUGFeRD PDF ready (InvoiceId={InvoiceId}, Size={Size}, Ms={Ms})",
            invoiceId,
            zugferdPdf.Length,
            sw.ElapsedMilliseconds);

        return new EmailAttachment
        {
            FileName = $"Rechnung_{invoiceNumber}_zugferd.pdf",
            Content = zugferdPdf,
            ContentType = "application/pdf"
        };
    }

    private async Task<EmailAttachment> BuildXRechnungXmlAsync(int invoiceId, string invoiceNumber)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("EmailAttachmentBuilder: generating XRechnung XML (InvoiceId={InvoiceId})", invoiceId);
        var xmlContent = await _xRechnungService.GenerateXRechnungXmlAsync(invoiceId);
        var xmlBytes = System.Text.Encoding.UTF8.GetBytes(xmlContent);
        _logger.LogInformation(
            "EmailAttachmentBuilder: XRechnung XML ready (InvoiceId={InvoiceId}, Size={Size}, Ms={Ms})",
            invoiceId,
            xmlBytes.Length,
            sw.ElapsedMilliseconds);

        return new EmailAttachment
        {
            FileName = $"Rechnung_{invoiceNumber}_xrechnung.xml",
            Content = xmlBytes,
            ContentType = "application/xml"
        };
    }

    private async Task<List<EmailAttachment>> BuildCustomAttachmentsAsync(int invoiceId)
    {
        _logger.LogInformation("EmailAttachmentBuilder: loading custom attachments (InvoiceId={InvoiceId})", invoiceId);
        var attachments = await _dbContext.InvoiceAttachments
            .AsNoTracking()
            .Where(a => a.InvoiceId == invoiceId && !a.IsFrozenSnapshot)
            .ToListAsync();

        return attachments
            .Select(a => new EmailAttachment
            {
                FileName = a.FileName,
                Content = a.Data,
                ContentType = string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType
            })
            .ToList();
    }
}
