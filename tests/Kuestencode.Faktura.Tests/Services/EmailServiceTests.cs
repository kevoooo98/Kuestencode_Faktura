using FluentAssertions;
using Kuestencode.Core.Interfaces;
using Kuestencode.Faktura.Data.Repositories;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services;
using Kuestencode.Faktura.Services.Email;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Host;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Kuestencode.Faktura.Tests.Services;

public class EmailServiceTests
{
    private readonly Mock<IInvoiceRepository> _invoiceRepository = new();
    private readonly Mock<IHostApiClient> _hostApiClient = new();
    private readonly Mock<IEmailMessageBuilder> _messageBuilder = new();
    private readonly Mock<IEmailEngine> _emailEngine = new();
    private readonly Mock<IPdfGeneratorService> _pdfGeneratorService = new();
    private readonly EmailService _service;

    public EmailServiceTests()
    {
        _pdfGeneratorService.Setup(p => p.FreezeSnapshotAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        _service = new EmailService(
            _invoiceRepository.Object, _hostApiClient.Object, _messageBuilder.Object, _emailEngine.Object,
            _pdfGeneratorService.Object, NullLogger<EmailService>.Instance);
    }

    private static CompanyDto MakeCompanyDto() => new()
    {
        OwnerFullName = "Max Mustermann",
        Email = "info@kuestencode.de"
    };

    private static Invoice MakeInvoice(InvoiceStatus status = InvoiceStatus.Draft) => new()
    {
        Id = 1,
        InvoiceNumber = "R-2026-0001",
        CustomerId = 1,
        Status = status,
        EmailSendCount = 0
    };

    private void SetupHappyPath(Invoice invoice, EmailMessage? message = null)
    {
        _hostApiClient.Setup(h => h.GetCompanyAsync()).ReturnsAsync(MakeCompanyDto());
        _invoiceRepository.Setup(r => r.GetWithDetailsAsync(1)).ReturnsAsync(invoice);
        _messageBuilder
            .Setup(m => m.BuildInvoiceEmailAsync(
                invoice, It.IsAny<Kuestencode.Core.Models.Company>(), "kunde@example.com",
                null, EmailAttachmentFormat.NormalPdf, null, null))
            .ReturnsAsync(message ?? new EmailMessage
            {
                RecipientEmail = "kunde@example.com",
                Subject = "Ihre Rechnung R-2026-0001",
                ContentHtml = "<p/>",
                Attachments = []
            });
        _emailEngine
            .Setup(e => e.SendEmailAsync(
                "kunde@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<Kuestencode.Core.Interfaces.EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), true))
            .ReturnsAsync(true);
        _invoiceRepository.Setup(r => r.UpdateAsync(It.IsAny<Invoice>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task SendInvoiceEmail_GlueckspfadMitDraftRechnung_SetztStatusAufSentUndAktualisiertTracking()
    {
        var invoice = MakeInvoice(InvoiceStatus.Draft);
        SetupHappyPath(invoice);

        var result = await _service.SendInvoiceEmailAsync(1, "kunde@example.com");

        result.Should().BeTrue();
        invoice.Status.Should().Be(InvoiceStatus.Sent);
        invoice.EmailSentTo.Should().Be("kunde@example.com");
        invoice.EmailSendCount.Should().Be(1);
        invoice.EmailSentAt.Should().NotBeNull();
        _invoiceRepository.Verify(r => r.UpdateAsync(invoice), Times.Once);
    }

    [Fact]
    public async Task SendInvoiceEmail_RechnungBereitsVersendet_BleibtStatusSentUndZaehlerErhoeht()
    {
        var invoice = MakeInvoice(InvoiceStatus.Sent);
        invoice.EmailSendCount = 2;
        SetupHappyPath(invoice);

        await _service.SendInvoiceEmailAsync(1, "kunde@example.com");

        invoice.Status.Should().Be(InvoiceStatus.Sent);
        invoice.EmailSendCount.Should().Be(3);
    }

    [Fact]
    public async Task SendInvoiceEmail_LeereEmpfaengerAdresse_WirftArgumentException()
    {
        var act = () => _service.SendInvoiceEmailAsync(1, "");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendInvoiceEmail_FirmaNichtGefunden_WirftInvalidOperationException()
    {
        _hostApiClient.Setup(h => h.GetCompanyAsync()).ReturnsAsync((CompanyDto?)null);

        var act = () => _service.SendInvoiceEmailAsync(1, "kunde@example.com");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Firmendaten*");
    }

    [Fact]
    public async Task SendInvoiceEmail_RechnungNichtGefunden_WirftInvalidOperationException()
    {
        _hostApiClient.Setup(h => h.GetCompanyAsync()).ReturnsAsync(MakeCompanyDto());
        _invoiceRepository.Setup(r => r.GetWithDetailsAsync(1)).ReturnsAsync((Invoice?)null);

        var act = () => _service.SendInvoiceEmailAsync(1, "kunde@example.com");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*1*");
    }

    [Fact]
    public async Task SendInvoiceEmail_UebergibtCcUndBccAnMessageBuilder()
    {
        var invoice = MakeInvoice();
        _hostApiClient.Setup(h => h.GetCompanyAsync()).ReturnsAsync(MakeCompanyDto());
        _invoiceRepository.Setup(r => r.GetWithDetailsAsync(1)).ReturnsAsync(invoice);
        _invoiceRepository.Setup(r => r.UpdateAsync(It.IsAny<Invoice>())).Returns(Task.CompletedTask);
        _messageBuilder
            .Setup(m => m.BuildInvoiceEmailAsync(
                invoice, It.IsAny<Kuestencode.Core.Models.Company>(), "kunde@example.com",
                "Custom", EmailAttachmentFormat.ZugferdPdf, "cc@example.com", "bcc@example.com"))
            .ReturnsAsync(new EmailMessage { RecipientEmail = "kunde@example.com" });
        _emailEngine
            .Setup(e => e.SendEmailAsync(
                "kunde@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<Kuestencode.Core.Interfaces.EmailAttachment>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), true))
            .ReturnsAsync(true);

        await _service.SendInvoiceEmailAsync(
            1, "kunde@example.com", "Custom", EmailAttachmentFormat.ZugferdPdf, "cc@example.com", "bcc@example.com");

        invoice.EmailCcRecipients.Should().Be("cc@example.com");
        invoice.EmailBccRecipients.Should().Be("bcc@example.com");
    }

    [Fact]
    public async Task SendInvoiceEmail_NormalPdf_FriertSnapshotEin()
    {
        var invoice = MakeInvoice(InvoiceStatus.Draft);
        SetupHappyPath(invoice);

        await _service.SendInvoiceEmailAsync(1, "kunde@example.com");

        _pdfGeneratorService.Verify(p => p.FreezeSnapshotAsync(1), Times.Once);
    }

    [Fact]
    public async Task TestEmailConnectionAsync_DelegiertAnEmailEngine()
    {
        _emailEngine.Setup(e => e.TestConnectionAsync()).ReturnsAsync((true, (string?)null));

        var (success, errorMessage) = await _service.TestEmailConnectionAsync();

        success.Should().BeTrue();
        errorMessage.Should().BeNull();
    }
}
