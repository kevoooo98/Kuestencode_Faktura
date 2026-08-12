using Kuestencode.Core.Interfaces;
using Kuestencode.Core.Models;
using Kuestencode.Core.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Kuestencode.Werkbank.Host.Services;

namespace Kuestencode.Werkbank.Host.Services.Email;

/// <summary>
/// Generische Email-Engine für plattformweiten E-Mail-Versand.
/// Wickelt jeden Inhalt über <see cref="EmailTemplateRenderer"/> in das eine, firmenweite
/// Layout — es gibt bewusst kein modul- oder layoutspezifisches Styling mehr.
/// </summary>
public class EmailEngine : IEmailEngine
{
    private readonly ICompanyService _companyService;
    private readonly IEnumerable<IEmailTemplateProvider> _templateProviders;
    private readonly ILogger<EmailEngine> _logger;
    private readonly PasswordEncryptionService _passwordEncryption;

    public EmailEngine(
        ICompanyService companyService,
        IEnumerable<IEmailTemplateProvider> templateProviders,
        ILogger<EmailEngine> logger,
        PasswordEncryptionService passwordEncryption)
    {
        _companyService = companyService;
        _templateProviders = templateProviders;
        _logger = logger;
        _passwordEncryption = passwordEncryption;
    }

    public async Task<bool> SendEmailAsync(
        string recipientEmail,
        string subject,
        string contentHtml,
        string? contentText = null,
        IEnumerable<EmailAttachment>? attachments = null,
        string? ccEmails = null,
        string? bccEmails = null,
        string? greeting = null,
        bool includeClosing = true)
    {
        try
        {
            var company = await _companyService.GetCompanyAsync();

            if (!company.IsEmailConfigured())
            {
                _logger.LogWarning("E-Mail-Versand fehlgeschlagen: SMTP nicht konfiguriert");
                return false;
            }

            var htmlBody = EmailTemplateRenderer.WrapHtml(company, contentHtml, greeting, includeClosing);
            var textBody = EmailTemplateRenderer.WrapText(company, contentText ?? StripHtml(contentHtml), greeting, includeClosing);

            var message = CreateMessage(
                company,
                recipientEmail,
                subject,
                htmlBody,
                textBody,
                attachments,
                ccEmails,
                bccEmails);

            await SendMessageAsync(company, message);

            _logger.LogInformation("E-Mail erfolgreich gesendet an {Recipient}", recipientEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Senden der E-Mail an {Recipient}", recipientEmail);
            return false;
        }
    }

    public async Task<bool> SendTemplatedEmailAsync<TContext>(
        string templateName,
        TContext context,
        string recipientEmail,
        string subject,
        IEnumerable<EmailAttachment>? attachments = null,
        string? ccEmails = null,
        string? bccEmails = null)
    {
        var provider = _templateProviders.FirstOrDefault(p => p.TemplateName == templateName);

        if (provider == null)
        {
            _logger.LogError("E-Mail-Template '{TemplateName}' nicht gefunden", templateName);
            return false;
        }

        var company = await _companyService.GetCompanyAsync();
        var htmlBody = provider.RenderHtml(context, company);
        var plainBody = provider.RenderPlainText(context, company);

        return await SendEmailAsync(
            recipientEmail,
            subject,
            htmlBody,
            plainBody,
            attachments,
            ccEmails,
            bccEmails);
    }

    public async Task<(bool Success, string? ErrorMessage)> TestConnectionAsync()
    {
        try
        {
            var company = await _companyService.GetCompanyAsync();

            if (string.IsNullOrWhiteSpace(company.SmtpHost) || !company.SmtpPort.HasValue)
            {
                return (false, "SMTP-Server nicht konfiguriert");
            }

            using var client = new SmtpClient();

            var secureSocketOptions = company.SmtpUseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(
                company.SmtpHost,
                company.SmtpPort.Value,
                secureSocketOptions);

            var decryptedPassword = _passwordEncryption.Decrypt(company.SmtpPassword ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(company.SmtpUsername) &&
                !string.IsNullOrWhiteSpace(decryptedPassword))
            {
                await client.AuthenticateAsync(company.SmtpUsername, decryptedPassword);
            }

            await client.DisconnectAsync(true);

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP-Verbindungstest fehlgeschlagen");
            return (false, ex.Message);
        }
    }

    // company.EmailSenderEmail/SmtpHost sind hier nie null: SendEmailAsync prüft company.IsEmailConfigured() vor dem Aufruf.
    private MimeMessage CreateMessage(
        Company company,
        string recipientEmail,
        string subject,
        string htmlBody,
        string? plainTextBody,
        IEnumerable<EmailAttachment>? attachments,
        string? ccEmails,
        string? bccEmails)
    {
        var message = new MimeMessage();

        // Sender
        var senderName = !string.IsNullOrWhiteSpace(company.EmailSenderName)
            ? company.EmailSenderName
            : company.DisplayName;

        message.From.Add(new MailboxAddress(senderName, company.EmailSenderEmail!));

        // Recipient
        message.To.Add(MailboxAddress.Parse(recipientEmail));

        // CC
        if (!string.IsNullOrWhiteSpace(ccEmails))
        {
            foreach (var cc in ccEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                message.Cc.Add(MailboxAddress.Parse(cc));
            }
        }

        // BCC
        if (!string.IsNullOrWhiteSpace(bccEmails))
        {
            foreach (var bcc in bccEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                message.Bcc.Add(MailboxAddress.Parse(bcc));
            }
        }

        message.Subject = subject;

        // Body
        var builder = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = plainTextBody ?? StripHtml(htmlBody)
        };

        // Attachments
        if (attachments != null)
        {
            foreach (var attachment in attachments)
            {
                builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
            }
        }

        message.Body = builder.ToMessageBody();

        return message;
    }

    private async Task SendMessageAsync(Company company, MimeMessage message)
    {
        using var client = new SmtpClient();

        var secureSocketOptions = company.SmtpUseSsl
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        await client.ConnectAsync(
            company.SmtpHost!,
            company.SmtpPort!.Value,
            secureSocketOptions);

        var decryptedPassword = _passwordEncryption.Decrypt(company.SmtpPassword ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(company.SmtpUsername) &&
            !string.IsNullOrWhiteSpace(decryptedPassword))
        {
            await client.AuthenticateAsync(company.SmtpUsername, decryptedPassword);
        }

        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private static string StripHtml(string html)
    {
        // Einfache HTML-zu-Text-Konvertierung
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }
}
