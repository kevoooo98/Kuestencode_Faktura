using InvoiceApp.Data.Repositories;
using InvoiceApp.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Globalization;

namespace InvoiceApp.Services;

public class EmailService : IEmailService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICompanyService _companyService;
    private readonly IPdfGeneratorService _pdfGenerator;
    private readonly IXRechnungService _xRechnungService;
    private readonly PasswordEncryptionService _passwordEncryption;
    private readonly ILogger<EmailService> _logger;
    private readonly IWebHostEnvironment _environment;

    public EmailService(
        IInvoiceRepository invoiceRepository,
        ICompanyService companyService,
        IPdfGeneratorService pdfGenerator,
        IXRechnungService xRechnungService,
        PasswordEncryptionService passwordEncryption,
        ILogger<EmailService> logger,
        IWebHostEnvironment environment)
    {
        _invoiceRepository = invoiceRepository;
        _companyService = companyService;
        _pdfGenerator = pdfGenerator;
        _xRechnungService = xRechnungService;
        _passwordEncryption = passwordEncryption;
        _logger = logger;
        _environment = environment;
    }

    public async Task<bool> SendInvoiceEmailAsync(
        int invoiceId,
        string recipientEmail,
        string? customMessage = null,
        EmailAttachmentFormat format = EmailAttachmentFormat.NormalPdf)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                throw new ArgumentException("Empfänger-E-Mail-Adresse ist erforderlich", nameof(recipientEmail));
            }

            // Load company with email settings
            var company = await _companyService.GetCompanyAsync();
            if (!IsEmailConfigured(company))
            {
                throw new InvalidOperationException("E-Mail-Versand ist nicht konfiguriert. Bitte konfigurieren Sie die E-Mail-Einstellungen.");
            }

            // Load invoice with details
            var invoice = await _invoiceRepository.GetWithDetailsAsync(invoiceId);
            if (invoice == null)
            {
                throw new InvalidOperationException($"Rechnung mit ID {invoiceId} nicht gefunden");
            }

            // Create email
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                !string.IsNullOrWhiteSpace(company.EmailSenderName) ? company.EmailSenderName : company.BusinessName ?? company.OwnerFullName,
                company.EmailSenderEmail ?? company.Email
            ));
            message.To.Add(MailboxAddress.Parse(recipientEmail));
            message.Subject = $"Ihre Rechnung {invoice.InvoiceNumber} - {message.From}";

            // Create email body
            var bodyBuilder = new BodyBuilder();

            // HTML Version
            bodyBuilder.HtmlBody = BuildHtmlEmailBody(invoice, company, customMessage);

            // Plain Text Fallback
            bodyBuilder.TextBody = BuildPlainTextEmailBody(invoice, company, customMessage);

            // Add attachments based on format
            switch (format)
            {
                case EmailAttachmentFormat.NormalPdf:
                    var normalPdf = _pdfGenerator.GenerateInvoicePdf(invoiceId);
                    bodyBuilder.Attachments.Add(
                        $"Rechnung_{invoice.InvoiceNumber}.pdf",
                        normalPdf,
                        new ContentType("application", "pdf"));
                    break;

                case EmailAttachmentFormat.ZugferdPdf:
                    var zugferdPdf = await _xRechnungService.GenerateZugferdPdfAsync(invoiceId);
                    bodyBuilder.Attachments.Add(
                        $"Rechnung_{invoice.InvoiceNumber}_zugferd.pdf",
                        zugferdPdf,
                        new ContentType("application", "pdf"));
                    break;

                case EmailAttachmentFormat.XRechnungXmlOnly:
                    var xmlContent = await _xRechnungService.GenerateXRechnungXmlAsync(invoiceId);
                    var xmlBytes = System.Text.Encoding.UTF8.GetBytes(xmlContent);
                    bodyBuilder.Attachments.Add(
                        $"Rechnung_{invoice.InvoiceNumber}_xrechnung.xml",
                        xmlBytes,
                        new ContentType("application", "xml"));
                    break;

                case EmailAttachmentFormat.XRechnungXmlAndPdf:
                    // Add XML
                    var xmlContent2 = await _xRechnungService.GenerateXRechnungXmlAsync(invoiceId);
                    var xmlBytes2 = System.Text.Encoding.UTF8.GetBytes(xmlContent2);
                    bodyBuilder.Attachments.Add(
                        $"Rechnung_{invoice.InvoiceNumber}_xrechnung.xml",
                        xmlBytes2,
                        new ContentType("application", "xml"));

                    // Add PDF
                    var separatePdf = _pdfGenerator.GenerateInvoicePdf(invoiceId);
                    bodyBuilder.Attachments.Add(
                        $"Rechnung_{invoice.InvoiceNumber}.pdf",
                        separatePdf,
                        new ContentType("application", "pdf"));
                    break;

                default:
                    throw new ArgumentException($"Unsupported format: {format}");
            }

            message.Body = bodyBuilder.ToMessageBody();

            // Send email
            using (var client = new SmtpClient())
            {
                // SMTP Authentication
                var decryptedPassword = _passwordEncryption.Decrypt(company.SmtpPassword!);
                await client.ConnectAsync(company.SmtpHost, company.SmtpPort!.Value,
                    company.SmtpUseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);

                if (!string.IsNullOrWhiteSpace(company.SmtpUsername) && !string.IsNullOrWhiteSpace(decryptedPassword))
                {
                    await client.AuthenticateAsync(company.SmtpUsername, decryptedPassword);
                }

                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }

            // Update Invoice with email tracking
            invoice.EmailSentAt = DateTime.UtcNow;
            invoice.EmailSentTo = recipientEmail;
            invoice.EmailSendCount++;

            // If status is Draft, change to Sent
            if (invoice.Status == InvoiceStatus.Draft)
            {
                invoice.Status = InvoiceStatus.Sent;
            }

            await _invoiceRepository.UpdateAsync(invoice);

            _logger.LogInformation(
                "Rechnung {InvoiceNumber} erfolgreich an {Email} versendet (Format: {Format})",
                invoice.InvoiceNumber,
                recipientEmail,
                format);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Versenden der Rechnung {InvoiceId} an {Email}", invoiceId, recipientEmail);
            throw;
        }
    }

    public async Task<(bool success, string? errorMessage)> TestEmailConnectionAsync(Company company)
    {
        try
        {
            if (!IsEmailConfigured(company))
            {
                return (false, "E-Mail-Einstellungen sind unvollständig. Bitte füllen Sie alle erforderlichen Felder aus.");
            }

            using var client = new SmtpClient();

            // Test SMTP connection
            var decryptedPassword = _passwordEncryption.Decrypt(company.SmtpPassword!);

            try
            {
                await client.ConnectAsync(company.SmtpHost, company.SmtpPort!.Value,
                    company.SmtpUseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None);
            }
            catch (Exception ex)
            {
                return (false, $"Verbindung zum Server fehlgeschlagen. Bitte prüfen Sie den SMTP-Server und Port.");
            }

            if (!string.IsNullOrWhiteSpace(company.SmtpUsername) && !string.IsNullOrWhiteSpace(decryptedPassword))
            {
                try
                {
                    await client.AuthenticateAsync(company.SmtpUsername, decryptedPassword);
                }
                catch (Exception ex)
                {
                    await client.DisconnectAsync(true);
                    return (false, $"Anmeldung fehlgeschlagen. Bitte prüfen Sie Benutzername und Passwort.");
                }
            }

            await client.DisconnectAsync(true);

            _logger.LogInformation("SMTP E-Mail-Konfigurationstest erfolgreich");
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E-Mail-Konfigurationstest fehlgeschlagen");
            return (false, $"Unerwarteter Fehler: {ex.Message}");
        }
    }

    private bool IsEmailConfigured(Company company)
    {
        // Check if SMTP is configured
        return !string.IsNullOrWhiteSpace(company.EmailSenderEmail) &&
               !string.IsNullOrWhiteSpace(company.SmtpHost) &&
               company.SmtpPort.HasValue &&
               !string.IsNullOrWhiteSpace(company.SmtpUsername) &&
               !string.IsNullOrWhiteSpace(company.SmtpPassword);
    }

    private string BuildHtmlEmailBody(Invoice invoice, Company company, string? customMessage)
    {
        var culture = new CultureInfo("de-DE");
        var formattedTotal = invoice.TotalGross.ToString("C", culture);
        var formattedDate = invoice.InvoiceDate.ToString("dd.MM.yyyy", culture);
        var formattedDueDate = invoice.DueDate?.ToString("dd.MM.yyyy", culture) ?? "Sofort fällig";

        // Select template based on EmailLayout
        var templateName = company.EmailLayout switch
        {
            EmailLayout.Klar => "EmailTemplateKlar.html",
            EmailLayout.Strukturiert => "EmailTemplateStrukturiert.html",
            EmailLayout.Betont => "EmailTemplateBetont.html",
            _ => "EmailTemplateKlar.html"
        };

        var templatePath = Path.Combine(_environment.WebRootPath, "templates", templateName);

        // Fallback to old HTML if template not found
        if (!File.Exists(templatePath))
        {
            _logger.LogWarning("Email template {TemplateName} not found, using fallback", templateName);
            return BuildFallbackHtmlEmailBody(invoice, company, customMessage);
        }

        var template = File.ReadAllText(templatePath);

        // Replace colors
        template = template.Replace("{{PRIMARY_COLOR}}", company.EmailPrimaryColor);
        template = template.Replace("{{ACCENT_COLOR}}", company.EmailAccentColor);

        // Prepare greeting and closing
        var greeting = string.IsNullOrWhiteSpace(company.EmailGreeting)
            ? "Sehr geehrte Damen und Herren,\n\nanbei erhalten Sie Ihre Rechnung."
            : company.EmailGreeting;

        var closing = string.IsNullOrWhiteSpace(company.EmailClosing)
            ? "Mit freundlichen Grüßen\n\n{{Firmenname}}"
            : company.EmailClosing;

        // Add custom message to greeting if provided
        if (!string.IsNullOrWhiteSpace(customMessage))
        {
            greeting = $"{greeting}\n\n{customMessage}";
        }

        // Replace placeholders in greeting and closing
        var firmenname = !string.IsNullOrEmpty(company.BusinessName)
            ? company.BusinessName
            : company.OwnerFullName;

        greeting = ReplaceEmailPlaceholders(greeting, invoice.InvoiceNumber, formattedDueDate, firmenname);
        closing = ReplaceEmailPlaceholders(closing, invoice.InvoiceNumber, formattedDueDate, firmenname);

        // Replace template placeholders
        template = template.Replace("{{GREETING}}", greeting.Replace("\n", "<br>"));
        template = template.Replace("{{CLOSING}}", closing.Replace("\n", "<br>"));
        template = template.Replace("{{INVOICE_NUMBER}}", invoice.InvoiceNumber);
        template = template.Replace("{{INVOICE_DATE}}", formattedDate);
        template = template.Replace("{{DUE_DATE}}", formattedDueDate);
        template = template.Replace("{{TOTAL_AMOUNT}}", formattedTotal);

        return template;
    }

    private string ReplaceEmailPlaceholders(string text, string invoiceNumber, string dueDate, string firmenname)
    {
        return text
            .Replace("{{Firmenname}}", firmenname)
            .Replace("{{Rechnungsnummer}}", invoiceNumber)
            .Replace("{{Faelligkeitsdatum}}", dueDate);
    }

    private string BuildFallbackHtmlEmailBody(Invoice invoice, Company company, string? customMessage)
    {
        var culture = new CultureInfo("de-DE");
        var formattedTotal = invoice.TotalGross.ToString("C", culture);
        var formattedDate = invoice.InvoiceDate.ToString("dd.MM.yyyy", culture);
        var formattedDueDate = invoice.DueDate?.ToString("dd.MM.yyyy", culture) ?? "Sofort fällig";

        var html = $@"
<!DOCTYPE html>
<html lang=""de"">
<head>
    <meta charset=""UTF-8"">
    <style>
        body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #0F2A3D; color: white; padding: 20px; text-align: center; }}
        .content {{ background-color: #f8f9fa; padding: 20px; margin: 20px 0; }}
        .details {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 3px solid #3FA796; }}
        .footer {{ text-align: center; color: #666; font-size: 12px; margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; }}
        .highlight {{ color: #0F2A3D; font-weight: bold; }}
        .bank-details {{ background-color: #fff; padding: 15px; margin: 15px 0; border: 1px solid #ddd; }}
        .signature {{ margin-top: 20px; white-space: pre-line; }}
        table {{ width: 100%; border-collapse: collapse; }}
        td {{ padding: 8px 0; }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>{company.BusinessName ?? company.OwnerFullName}</h1>
        </div>

        <div class=""content"">
            <p>Sehr geehrte Damen und Herren,</p>

            {(!string.IsNullOrWhiteSpace(customMessage) ? $"<p>{customMessage.Replace("\n", "<br>")}</p>" : "")}

            <p>anbei erhalten Sie die Rechnung <span class=""highlight"">{invoice.InvoiceNumber}</span>.</p>

            <div class=""details"">
                <table>
                    <tr>
                        <td><strong>Rechnungsbetrag:</strong></td>
                        <td class=""highlight"">{formattedTotal}</td>
                    </tr>
                    <tr>
                        <td><strong>Rechnungsnummer:</strong></td>
                        <td>{invoice.InvoiceNumber}</td>
                    </tr>
                    <tr>
                        <td><strong>Rechnungsdatum:</strong></td>
                        <td>{formattedDate}</td>
                    </tr>
                    <tr>
                        <td><strong>Fällig am:</strong></td>
                        <td>{formattedDueDate}</td>
                    </tr>
                </table>
            </div>

            <div class=""bank-details"">
                <h3 style=""margin-top: 0;"">Bankverbindung</h3>
                <table>
                    <tr>
                        <td><strong>Kontoinhaber:</strong></td>
                        <td>{company.AccountHolder ?? company.OwnerFullName}</td>
                    </tr>
                    <tr>
                        <td><strong>Bank:</strong></td>
                        <td>{company.BankName}</td>
                    </tr>
                    <tr>
                        <td><strong>IBAN:</strong></td>
                        <td>{company.BankAccount}</td>
                    </tr>
                    {(!string.IsNullOrWhiteSpace(company.Bic) ? $@"
                    <tr>
                        <td><strong>BIC:</strong></td>
                        <td>{company.Bic}</td>
                    </tr>" : "")}
                    <tr>
                        <td><strong>Verwendungszweck:</strong></td>
                        <td>{invoice.InvoiceNumber}</td>
                    </tr>
                </table>
            </div>

            <p>Die Rechnung finden Sie im Anhang dieser E-Mail als PDF-Datei.</p>

            {(!string.IsNullOrWhiteSpace(company.EmailSignature) ? $"<div class=\"signature\">{company.EmailSignature}</div>" : "<p>Vielen Dank für Ihren Auftrag!</p>")}
        </div>

        <div class=""footer"">
            <p><strong>{company.BusinessName ?? company.OwnerFullName}</strong></p>
            <p>{company.Address}, {company.PostalCode} {company.City}</p>
            {(!string.IsNullOrWhiteSpace(company.Phone) ? $"<p>Tel: {company.Phone}</p>" : "")}
            {(!string.IsNullOrWhiteSpace(company.Email) ? $"<p>E-Mail: {company.Email}</p>" : "")}
            {(!string.IsNullOrWhiteSpace(company.Website) ? $"<p>Web: {company.Website}</p>" : "")}
            {(!string.IsNullOrWhiteSpace(company.TaxNumber) ? $"<p>Steuernummer: {company.TaxNumber}</p>" : "")}
            {(!string.IsNullOrWhiteSpace(company.VatId) ? $"<p>USt-IdNr: {company.VatId}</p>" : "")}
        </div>
    </div>
</body>
</html>";

        return html;
    }

    private string BuildPlainTextEmailBody(Invoice invoice, Company company, string? customMessage)
    {
        var culture = new CultureInfo("de-DE");
        var formattedTotal = invoice.TotalGross.ToString("C", culture);
        var formattedDate = invoice.InvoiceDate.ToString("dd.MM.yyyy", culture);
        var formattedDueDate = invoice.DueDate?.ToString("dd.MM.yyyy", culture) ?? "Sofort fällig";

        var text = $@"
{company.BusinessName ?? company.OwnerFullName}
{new string('=', (company.BusinessName ?? company.OwnerFullName).Length)}

Sehr geehrte Damen und Herren,

{(!string.IsNullOrWhiteSpace(customMessage) ? $"{customMessage}\n\n" : "")}anbei erhalten Sie die Rechnung {invoice.InvoiceNumber}.

RECHNUNGSDETAILS:
------------------
Rechnungsbetrag: {formattedTotal}
Rechnungsnummer: {invoice.InvoiceNumber}
Rechnungsdatum:  {formattedDate}
Fällig am:       {formattedDueDate}

BANKVERBINDUNG:
---------------
Kontoinhaber:    {company.AccountHolder ?? company.OwnerFullName}
Bank:            {company.BankName}
IBAN:            {company.BankAccount}
{(!string.IsNullOrWhiteSpace(company.Bic) ? $"BIC:             {company.Bic}\n" : "")}Verwendungszweck: {invoice.InvoiceNumber}

Die Rechnung finden Sie im Anhang dieser E-Mail als PDF-Datei.

{(!string.IsNullOrWhiteSpace(company.EmailSignature) ? company.EmailSignature : "Vielen Dank für Ihren Auftrag!")}

--
{company.BusinessName ?? company.OwnerFullName}
{company.Address}
{company.PostalCode} {company.City}
{(!string.IsNullOrWhiteSpace(company.Phone) ? $"Tel: {company.Phone}\n" : "")}{(!string.IsNullOrWhiteSpace(company.Email) ? $"E-Mail: {company.Email}\n" : "")}{(!string.IsNullOrWhiteSpace(company.Website) ? $"Web: {company.Website}\n" : "")}{(!string.IsNullOrWhiteSpace(company.TaxNumber) ? $"Steuernummer: {company.TaxNumber}\n" : "")}{(!string.IsNullOrWhiteSpace(company.VatId) ? $"USt-IdNr: {company.VatId}\n" : "")}";

        return text.Trim();
    }
}
