using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services.Pdf.Components;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Kuestencode.Faktura.Services.Pdf.Layouts;

/// <summary>
/// "Strukturiert" layout - Structured design with boxes and borders.
/// </summary>
public class StrukturiertLayoutRenderer : BasePdfLayout
{
    public StrukturiertLayoutRenderer(
        PdfTemplateEngine templateEngine,
        PdfSummaryBlockBuilder summaryBlockBuilder,
        PdfDownPaymentDetailBuilder downPaymentDetailBuilder,
        PdfPaymentInfoBuilder paymentInfoBuilder)
        : base(templateEngine, summaryBlockBuilder, downPaymentDetailBuilder, paymentInfoBuilder)
    {
    }

    public override void RenderHeader(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            // Header with colored background
            column.Item().Background(company.PdfPrimaryColor).Padding(15).Row(row =>
            {
                row.RelativeItem().Column(leftColumn =>
                {
                    if (!string.IsNullOrEmpty(company.BusinessName))
                    {
                        leftColumn.Item().Text(company.BusinessName)
                            .FontSize(16)
                            .Bold()
                            .FontColor("#FFFFFF");
                        leftColumn.Item().Text(company.OwnerFullName)
                            .FontSize(12)
                            .FontColor("#FFFFFF");
                    }
                    else
                    {
                        leftColumn.Item().Text(company.OwnerFullName)
                            .FontSize(16)
                            .Bold()
                            .FontColor("#FFFFFF");
                    }
                });

                row.RelativeItem().AlignRight().Column(rightColumn =>
                {
                    if (company.LogoData != null && company.LogoData.Length > 0)
                    {
                        rightColumn.Item().MaxWidth(120).Image(company.LogoData);
                    }
                });
            });

            // Invoice info box
            column.Item().PaddingTop(10).Border(1).BorderColor(company.PdfAccentColor).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(leftCol =>
                {
                    leftCol.Item().Text($"{invoice.DocumentLabel} {invoice.InvoiceNumber}")
                        .FontSize(14)
                        .Bold()
                        .FontColor(company.PdfPrimaryColor);
                    leftCol.Item().Text($"Datum: {invoice.InvoiceDate:dd.MM.yyyy}").FontSize(10);
                    if (invoice.Customer != null)
                    {
                        leftCol.Item().Text($"Kundennr.: {invoice.Customer.CustomerNumber}").FontSize(10);
                    }
                    if (!string.IsNullOrWhiteSpace(invoice.ProjectName))
                    {
                        leftCol.Item().Text($"Projekt: {invoice.ProjectName}").FontSize(10);
                    }
                });

                row.RelativeItem().AlignRight().Column(rightCol =>
                {
                    if (invoice.DueDate.HasValue)
                    {
                        rightCol.Item().Text($"Fällig: {invoice.DueDate.Value:dd.MM.yyyy}")
                            .FontSize(10)
                            .Bold()
                            .FontColor(company.PdfAccentColor);
                    }
                });
            });
        });
    }

    public override void RenderContent(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            // Recipient address in box
            column.Item().PaddingTop(15).Border(1).BorderColor(DividerColor).Padding(10).Column(addressColumn =>
            {
                RenderRecipientAddress(addressColumn, invoice);
            });

            // Greeting
            column.Item().PaddingTop(20).Column(greetingColumn =>
            {
                RenderGreeting(greetingColumn, invoice, company);
            });

            // Items table with stronger structure
            RenderItemsTable(column, invoice, company, withBorder: true);

            // Summary block in box
            column.Item().PaddingTop(15).Element(c =>
                SummaryBlockBuilder.RenderWithBorder(c, invoice, company));

            // Down payment detail (Abschlagsrechnungstabelle / Zahlungsübersicht)
            column.Item().PaddingTop(15).Element(c =>
                DownPaymentDetailBuilder.Render(c, invoice, company));

            // Tax notices
            RenderKleinunternehmerNotice(column, company);
            RenderReverseChargeNotice(column, invoice, company);

            // Payment information with QR code
            column.Item().Element(c =>
                PaymentInfoBuilder.Render(c, invoice, company, bold: false));

            // Closing text
            RenderClosingText(column, invoice, company, bold: false);
        });
    }
}
