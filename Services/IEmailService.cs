using InvoiceApp.Models;

namespace InvoiceApp.Services;

public interface IEmailService
{
    Task<bool> SendInvoiceEmailAsync(
        int invoiceId,
        string recipientEmail,
        string? customMessage = null,
        EmailAttachmentFormat format = EmailAttachmentFormat.NormalPdf,
        string? ccEmails = null,
        string? bccEmails = null);
    Task<(bool success, string? errorMessage)> TestEmailConnectionAsync(Company company);
}
