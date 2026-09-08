using FluentAssertions;
using Kuestencode.Core.Interfaces;
using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services.Email;
using Moq;
using Xunit;

namespace Kuestencode.Faktura.Tests.Services.Email;

public class EmailMessageBuilderTests
{
    private readonly Mock<IEmailTemplateRenderer> _templateRenderer = new();
    private readonly Mock<IEmailAttachmentBuilder> _attachmentBuilder = new();
    private readonly EmailMessageBuilder _builder;

    public EmailMessageBuilderTests()
    {
        _builder = new EmailMessageBuilder(_templateRenderer.Object, _attachmentBuilder.Object);
    }

    private static Invoice MakeInvoice() =>
        new() { Id = 1, InvoiceNumber = "R-2026-0001" };

    [Fact]
    public async Task BuildInvoiceEmailAsync_NutztEmailSenderName_WennGesetzt()
    {
        var invoice = MakeInvoice();
        var company = new Company { EmailSenderName = "Buchhaltung Kuestencode", BusinessName = "Kuestencode GmbH" };

        _templateRenderer.Setup(r => r.RenderContentHtml(invoice, company)).Returns("<html/>");
        _templateRenderer.Setup(r => r.RenderContentText(invoice, company)).Returns("text");
        _attachmentBuilder
            .Setup(a => a.BuildInvoiceAttachmentsAsync(1, "R-2026-0001", EmailAttachmentFormat.NormalPdf))
            .ReturnsAsync([]);

        var message = await _builder.BuildInvoiceEmailAsync(
            invoice, company, "kunde@example.com", null, EmailAttachmentFormat.NormalPdf, null, null);

        message.Subject.Should().Be("Ihre Rechnung R-2026-0001 - Buchhaltung Kuestencode");
    }

    [Fact]
    public async Task BuildInvoiceEmailAsync_FaelltAufBusinessName_ZurueckWennKeinEmailSenderName()
    {
        var invoice = MakeInvoice();
        var company = new Company { BusinessName = "Kuestencode GmbH", OwnerFullName = "Max Mustermann" };

        _templateRenderer.Setup(r => r.RenderContentHtml(invoice, company)).Returns("<html/>");
        _templateRenderer.Setup(r => r.RenderContentText(invoice, company)).Returns("text");
        _attachmentBuilder
            .Setup(a => a.BuildInvoiceAttachmentsAsync(1, "R-2026-0001", EmailAttachmentFormat.NormalPdf))
            .ReturnsAsync([]);

        var message = await _builder.BuildInvoiceEmailAsync(
            invoice, company, "kunde@example.com", null, EmailAttachmentFormat.NormalPdf, null, null);

        message.Subject.Should().Be("Ihre Rechnung R-2026-0001 - Kuestencode GmbH");
    }

    [Fact]
    public async Task BuildInvoiceEmailAsync_FaelltAufOwnerFullName_ZurueckWennWederEmailSenderNameNochBusinessName()
    {
        var invoice = MakeInvoice();
        var company = new Company { OwnerFullName = "Max Mustermann" };

        _templateRenderer.Setup(r => r.RenderContentHtml(invoice, company)).Returns("<html/>");
        _templateRenderer.Setup(r => r.RenderContentText(invoice, company)).Returns("text");
        _attachmentBuilder
            .Setup(a => a.BuildInvoiceAttachmentsAsync(1, "R-2026-0001", EmailAttachmentFormat.NormalPdf))
            .ReturnsAsync([]);

        var message = await _builder.BuildInvoiceEmailAsync(
            invoice, company, "kunde@example.com", null, EmailAttachmentFormat.NormalPdf, null, null);

        message.Subject.Should().Be("Ihre Rechnung R-2026-0001 - Max Mustermann");
    }

    [Fact]
    public async Task BuildInvoiceEmailAsync_UebernimmtCcBccUndGreetingUndAttachments()
    {
        var invoice = MakeInvoice();
        var company = new Company { OwnerFullName = "Max Mustermann" };
        var attachments = new List<EmailAttachment> { new() { FileName = "R-2026-0001.pdf" } };

        _templateRenderer.Setup(r => r.RenderContentHtml(invoice, company)).Returns("<html/>");
        _templateRenderer.Setup(r => r.RenderContentText(invoice, company)).Returns("text");
        _templateRenderer.Setup(r => r.ResolveGreeting(invoice, "Custom Message")).Returns("Custom Message");
        _attachmentBuilder
            .Setup(a => a.BuildInvoiceAttachmentsAsync(1, "R-2026-0001", EmailAttachmentFormat.ZugferdPdf))
            .ReturnsAsync(attachments);

        var message = await _builder.BuildInvoiceEmailAsync(
            invoice, company, "kunde@example.com", "Custom Message", EmailAttachmentFormat.ZugferdPdf,
            "cc@example.com", "bcc@example.com");

        message.RecipientEmail.Should().Be("kunde@example.com");
        message.CcEmails.Should().Be("cc@example.com");
        message.BccEmails.Should().Be("bcc@example.com");
        message.Greeting.Should().Be("Custom Message");
        message.Attachments.Should().BeSameAs(attachments);
        message.ContentHtml.Should().Be("<html/>");
        message.ContentText.Should().Be("text");
    }
}
