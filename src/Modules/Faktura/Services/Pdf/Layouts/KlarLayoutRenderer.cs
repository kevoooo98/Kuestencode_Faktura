using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services.Pdf.Components;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Kuestencode.Faktura.Services.Pdf.Layouts;

/// <summary>
/// "Klar" layout - Clean and simple design with minimal visual elements.
/// </summary>
public class KlarLayoutRenderer : BasePdfLayout
{
    public KlarLayoutRenderer(
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
            column.Item().Row(row =>
            {
                // Left side: Company data
                row.RelativeItem().Column(leftColumn =>
                {
                    if (!string.IsNullOrEmpty(company.BusinessName))
                    {
                        leftColumn.Item().Text(company.BusinessName)
                            .FontSize(16)
                            .Bold()
                            .FontColor(company.PdfPrimaryColor);
                        leftColumn.Item().Text(company.OwnerFullName)
                            .FontSize(12)
                            .FontColor(TextSecondaryColor);
                    }
                    else
                    {
                        leftColumn.Item().Text(company.OwnerFullName)
                            .FontSize(16)
                            .Bold()
                            .FontColor(company.PdfPrimaryColor);
                    }

                    leftColumn.Item().PaddingTop(5).Text(company.Address)
                        .FontSize(9)
                        .FontColor(TextSecondaryColor);
                    leftColumn.Item().Text($"{company.PostalCode} {company.City}")
                        .FontSize(9)
                        .FontColor(TextSecondaryColor);

                    if (!string.IsNullOrEmpty(company.Email))
                    {
                        leftColumn.Item().PaddingTop(3).Text(company.Email)
                            .FontSize(9)
                            .FontColor(TextSecondaryColor);
                    }
                    if (!string.IsNullOrEmpty(company.Phone))
                    {
                        leftColumn.Item().Text(company.Phone)
                            .FontSize(9)
                            .FontColor(TextSecondaryColor);
                    }
                });

                // Right side: Logo (if available) + Invoice metadata
                row.RelativeItem().AlignRight().Column(rightColumn =>
                {
                    // Show logo if available
                    if (company.LogoData != null && company.LogoData.Length > 0)
                    {
                        rightColumn.Item().MaxWidth(150).Image(company.LogoData);
                        rightColumn.Item().PaddingBottom(10);
                    }

                    rightColumn.Item().Text($"{invoice.DocumentLabel} {invoice.InvoiceNumber}")
                        .FontSize(16)
                        .Bold()
                        .FontColor(company.PdfPrimaryColor);

                    rightColumn.Item().PaddingTop(5).Text($"Datum: {invoice.InvoiceDate:dd.MM.yyyy}")
                        .FontSize(10);

                    if (invoice.Customer != null)
                    {
                        rightColumn.Item().Text($"Kundennr.: {invoice.Customer.CustomerNumber}")
                            .FontSize(10);
                    }

                    if (invoice.DueDate.HasValue)
                    {
                        rightColumn.Item().Text($"Fällig: {invoice.DueDate.Value:dd.MM.yyyy}")
                            .FontSize(10)
                            .Bold();
                    }

                    if (invoice.ServicePeriodStart.HasValue && invoice.ServicePeriodEnd.HasValue)
                    {
                        rightColumn.Item().PaddingTop(3).Text($"Leistungszeitraum:")
                            .FontSize(9)
                            .FontColor(TextSecondaryColor);
                        rightColumn.Item().Text($"{invoice.ServicePeriodStart.Value:dd.MM.yyyy} - {invoice.ServicePeriodEnd.Value:dd.MM.yyyy}")
                            .FontSize(9);
                    }

                    if (!string.IsNullOrWhiteSpace(invoice.ProjectName))
                    {
                        rightColumn.Item().PaddingTop(3).Text($"Projekt: {invoice.ProjectName}")
                            .FontSize(9);
                    }
                });
            });

            // Divider line
            column.Item().PaddingTop(15).PaddingBottom(10)
                .BorderBottom(1)
                .BorderColor(DividerColor);
        });
    }

    public override void RenderContent(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            // Recipient address
            column.Item().PaddingTop(20).Column(addressColumn =>
            {
                RenderRecipientAddress(addressColumn, invoice);
            });

            // Greeting
            column.Item().PaddingTop(30).Column(greetingColumn =>
            {
                RenderGreeting(greetingColumn, invoice, company);
            });

            // Items table
            RenderItemsTable(column, invoice, company, withBorder: false);

            // Summary block
            column.Item().PaddingTop(15).Element(c =>
                SummaryBlockBuilder.RenderStandard(c, invoice, company));

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
