using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services.Pdf.Components;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Kuestencode.Faktura.Services.Pdf.Layouts;

/// <summary>
/// "Betont" layout - Bold design with colored header and emphasized elements.
/// </summary>
public class BetontLayoutRenderer : BasePdfLayout
{
    public BetontLayoutRenderer(
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
            // Large colored header
            column.Item().Background(company.PdfPrimaryColor).Padding(20).Column(headerCol =>
            {
                headerCol.Item().Row(row =>
                {
                    row.RelativeItem().Column(leftColumn =>
                    {
                        if (!string.IsNullOrEmpty(company.BusinessName))
                        {
                            leftColumn.Item().Text(company.BusinessName)
                                .FontSize(18)
                                .Bold()
                                .FontColor("#FFFFFF");
                            leftColumn.Item().Text(company.OwnerFullName)
                                .FontSize(13)
                                .FontColor("#FFFFFF");
                        }
                        else
                        {
                            leftColumn.Item().Text(company.OwnerFullName)
                                .FontSize(18)
                                .Bold()
                                .FontColor("#FFFFFF");
                        }

                        leftColumn.Item().PaddingTop(8).Text(company.Address).FontSize(9).FontColor("#FFFFFF");
                        leftColumn.Item().Text($"{company.PostalCode} {company.City}").FontSize(9).FontColor("#FFFFFF");
                    });

                    row.RelativeItem().AlignRight().Column(rightColumn =>
                    {
                        if (company.LogoData != null && company.LogoData.Length > 0)
                        {
                            rightColumn.Item().MaxWidth(130).Image(company.LogoData);
                        }
                    });
                });

                // Invoice number highlighted
                headerCol.Item().PaddingTop(15).Background(company.PdfAccentColor).Padding(10).Text($"{invoice.DocumentLabel.ToUpperInvariant()} {invoice.InvoiceNumber}")
                    .FontSize(16)
                    .Bold()
                    .FontColor("#FFFFFF")
                    .AlignCenter();
            });

            // Metadata box
            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(leftCol =>
                {
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
                        rightCol.Item().Background(company.PdfAccentColor).Padding(5).Text($"Fällig: {invoice.DueDate.Value:dd.MM.yyyy}")
                            .FontSize(11)
                            .Bold()
                            .FontColor("#FFFFFF");
                    }
                });
            });
        });
    }

    public override void RenderContent(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            // Recipient address with accent
            column.Item().PaddingTop(15).BorderLeft(3).BorderColor(company.PdfAccentColor).PaddingLeft(10).Column(addressColumn =>
            {
                if (invoice.Customer != null)
                {
                    addressColumn.Item().Text(invoice.Customer.Name).FontSize(11).Bold().FontColor(company.PdfPrimaryColor);
                    addressColumn.Item().Text(invoice.Customer.Address).FontSize(10);
                    addressColumn.Item().Text($"{invoice.Customer.PostalCode} {invoice.Customer.City}").FontSize(10);
                }
            });

            // Greeting
            column.Item().PaddingTop(20).Column(greetingColumn =>
            {
                RenderGreeting(greetingColumn, invoice, company);
            });

            // Items table
            RenderItemsTable(column, invoice, company, withBorder: false);

            // Summary block highlighted
            column.Item().PaddingTop(15).Element(c =>
                SummaryBlockBuilder.RenderWithBackground(c, invoice, company));

            // Down payment detail (Abschlagsrechnungstabelle / Zahlungsübersicht)
            column.Item().PaddingTop(15).Element(c =>
                DownPaymentDetailBuilder.Render(c, invoice, company));

            // Tax notices
            RenderKleinunternehmerNotice(column, company);
            RenderReverseChargeNotice(column, invoice, company);

            // Payment information with QR code (bold text)
            column.Item().Element(c =>
                PaymentInfoBuilder.Render(c, invoice, company, bold: true));

            // Closing text (bold)
            RenderClosingText(column, invoice, company, bold: true);
        });
    }
}
