using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services.Pdf.Components;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace Kuestencode.Faktura.Services.Pdf.Layouts;

/// <summary>
/// Base class for PDF layout renderers containing shared functionality.
/// </summary>
public abstract class BasePdfLayout : IPdfLayoutRenderer
{
    protected readonly PdfTemplateEngine TemplateEngine;
    protected readonly PdfSummaryBlockBuilder SummaryBlockBuilder;
    protected readonly PdfDownPaymentDetailBuilder DownPaymentDetailBuilder;
    protected readonly PdfPaymentInfoBuilder PaymentInfoBuilder;
    protected readonly CultureInfo GermanCulture = new CultureInfo("de-DE");

    // Shared color constants
    protected const string TextPrimaryColor = "#1A1A1A";
    protected const string TextSecondaryColor = "#6B7280";
    protected const string BackgroundColor = "#F4F6F8";
    protected const string DividerColor = "#E5E7EB";

    protected BasePdfLayout(
        PdfTemplateEngine templateEngine,
        PdfSummaryBlockBuilder summaryBlockBuilder,
        PdfDownPaymentDetailBuilder downPaymentDetailBuilder,
        PdfPaymentInfoBuilder paymentInfoBuilder)
    {
        TemplateEngine = templateEngine;
        SummaryBlockBuilder = summaryBlockBuilder;
        DownPaymentDetailBuilder = downPaymentDetailBuilder;
        PaymentInfoBuilder = paymentInfoBuilder;
    }

    public abstract void RenderHeader(IContainer container, Invoice invoice, Company company);
    public abstract void RenderContent(IContainer container, Invoice invoice, Company company);

    public virtual void RenderFooter(IContainer container, Company company)
    {
        container.AlignCenter().Column(column =>
        {
            column.Item().BorderTop(1).BorderColor(DividerColor).PaddingTop(10);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(leftColumn =>
                {
                    // Bei Kleinunternehmern muss der vollständige bürgerliche Name im Footer stehen
                    leftColumn.Item().Text(company.OwnerFullName).FontSize(8).FontColor(TextSecondaryColor);
                    var taxLine = GetTaxIdentifierLine(company);
                    if (!string.IsNullOrWhiteSpace(taxLine))
                    {
                        leftColumn.Item().Text(taxLine).FontSize(8).FontColor(TextSecondaryColor);
                    }
                    if (!string.IsNullOrWhiteSpace(company.FooterText))
                    {
                        leftColumn.Item().Text(text =>
                        {
                            text.DefaultTextStyle(x => x.FontSize(8).FontColor(TextSecondaryColor));
                            AppendMultilineText(text, company.FooterText, 8);
                        });
                    }
                });

                row.RelativeItem().AlignCenter().Column(centerColumn =>
                {
                    centerColumn.Item().Text(text =>
                    {
                        text.CurrentPageNumber().FontSize(8).FontColor(TextSecondaryColor);
                        text.Span(" / ").FontSize(8).FontColor(TextSecondaryColor);
                        text.TotalPages().FontSize(8).FontColor(TextSecondaryColor);
                    });
                });

                row.RelativeItem().AlignRight().Column(rightColumn =>
                {
                    rightColumn.Item().Text(company.BankName).FontSize(8).FontColor(TextSecondaryColor);
                    rightColumn.Item().Text($"IBAN: {company.BankAccount}").FontSize(8).FontColor(TextSecondaryColor);

                    foreach (var additional in company.AdditionalBankAccounts.OrderBy(a => a.SortOrder))
                    {
                        rightColumn.Item().PaddingTop(4).Text(additional.BankName).FontSize(8).FontColor(TextSecondaryColor);
                        rightColumn.Item().Text($"IBAN: {additional.Iban}").FontSize(8).FontColor(TextSecondaryColor);
                    }
                });
            });
        });
    }

    /// <summary>
    /// Renders the recipient address section.
    /// </summary>
    protected void RenderRecipientAddress(ColumnDescriptor column, Invoice invoice)
    {
        if (invoice.Customer != null)
        {
            column.Item().Text(invoice.Customer.Name)
                .FontSize(11)
                .Bold();
            column.Item().Text(invoice.Customer.Address)
                .FontSize(10);
            column.Item().Text($"{invoice.Customer.PostalCode} {invoice.Customer.City}")
                .FontSize(10);
        }
    }

    /// <summary>
    /// Renders the greeting and introduction text.
    /// </summary>
    protected void RenderGreeting(ColumnDescriptor column, Invoice invoice, Company company)
    {
        var headerText = invoice.Type == InvoiceType.CreditNote
            ? company.PdfCreditNoteHeaderText
            : company.PdfHeaderText;

        if (!string.IsNullOrWhiteSpace(headerText))
        {
            column.Item().Text(TemplateEngine.ReplacePlaceholders(headerText, invoice, company))
                .FontSize(10);
        }
        else
        {
            // Use customer-specific salutation if available, otherwise default
            var salutation = !string.IsNullOrWhiteSpace(invoice.Customer?.Salutation)
                ? invoice.Customer.Salutation
                : "Sehr geehrte Damen und Herren,";

            column.Item().Text(salutation)
                .FontSize(10);

            var intro = invoice.Type == InvoiceType.CreditNote
                ? "hiermit schreiben wir Ihnen folgende Positionen gut:"
                : "hiermit stellen wir Ihnen folgende Leistungen in Rechnung:";
            column.Item().PaddingTop(10).Text(intro)
                .FontSize(10);
        }
    }

    /// <summary>
    /// Renders the items table with positions, descriptions, quantities, prices.
    /// </summary>
    protected void RenderItemsTable(ColumnDescriptor column, Invoice invoice, Company company, bool withBorder = false)
    {
        var tableContainer = column.Item().PaddingTop(20);

        if (withBorder)
        {
            tableContainer = tableContainer.Border(1).BorderColor(company.PdfPrimaryColor);
        }

        var orderedItems = invoice.Items.OrderBy(i => i.Position).ToList();
        var sections = BuildSections(orderedItems);
        var hasHeaders = sections.Any(s => s.Header != null);

        tableContainer.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(40);  // Pos.
                columns.RelativeColumn(4);   // Beschreibung
                columns.ConstantColumn(60);  // Menge
                columns.ConstantColumn(80);  // Einzelpreis
                columns.ConstantColumn(80);  // Gesamtpreis
            });

            // Header row
            table.Header(header =>
            {
                header.Cell().Background(company.PdfPrimaryColor)
                    .Padding(5).Text("Pos.").FontColor("#FFFFFF").FontSize(9).Bold();
                header.Cell().Background(company.PdfPrimaryColor)
                    .Padding(5).Text("Beschreibung").FontColor("#FFFFFF").FontSize(9).Bold();
                header.Cell().Background(company.PdfPrimaryColor)
                    .Padding(5).AlignRight().Text("Menge").FontColor("#FFFFFF").FontSize(9).Bold();
                header.Cell().Background(company.PdfPrimaryColor)
                    .Padding(5).AlignRight().Text("Einzelpreis").FontColor("#FFFFFF").FontSize(9).Bold();
                header.Cell().Background(company.PdfPrimaryColor)
                    .Padding(5).AlignRight().Text("Gesamtpreis").FontColor("#FFFFFF").FontSize(9).Bold();
            });

            int positionCounter = 0;
            foreach (var section in sections)
            {
                if (section.Header != null)
                {
                    // Section header row spanning all 5 columns
                    table.Cell().ColumnSpan(5)
                        .BorderTop(1).BorderColor(DividerColor)
                        .Background(BackgroundColor).Padding(5)
                        .Text(section.Header.Description).Bold().FontSize(9);
                }

                for (int i = 0; i < section.Items.Count; i++)
                {
                    var item = section.Items[i];
                    positionCounter++;
                    var bgColor = positionCounter % 2 == 0 ? BackgroundColor : "#FFFFFF";

                    if (withBorder)
                        RenderItemRowWithBorder(table, item, bgColor, positionCounter);
                    else
                        RenderItemRow(table, item, bgColor, positionCounter);
                }

                // Section subtotal after each titled section
                if (hasHeaders && section.Header != null && section.Items.Count > 0)
                {
                    var subtotal = section.Items.Sum(i => i.TotalNet);
                    table.Cell().ColumnSpan(4).AlignRight().PaddingRight(5).PaddingTop(3).PaddingBottom(3)
                        .Text("Zwischensumme:").FontSize(9).Italic().FontColor(TextSecondaryColor);
                    table.Cell().AlignRight().BorderTop(1).BorderColor(DividerColor).Padding(3)
                        .Text(subtotal.ToString("C2", GermanCulture)).FontSize(9).Bold();
                }
            }
        });
    }

    private static List<(InvoiceItem? Header, List<InvoiceItem> Items)> BuildSections(List<InvoiceItem> orderedItems)
    {
        var sections = new List<(InvoiceItem? Header, List<InvoiceItem> Items)>();
        InvoiceItem? currentHeader = null;
        var currentItems = new List<InvoiceItem>();

        foreach (var item in orderedItems)
        {
            if (item.IsHeader)
            {
                sections.Add((currentHeader, currentItems));
                currentHeader = item;
                currentItems = new List<InvoiceItem>();
            }
            else
            {
                currentItems.Add(item);
            }
        }
        sections.Add((currentHeader, currentItems));
        return sections.Where(s => s.Header != null || s.Items.Count > 0).ToList();
    }

    private void RenderItemRow(TableDescriptor table, InvoiceItem item, string bgColor, int displayPosition)
    {
        var mengeText = string.IsNullOrWhiteSpace(item.Unit)
            ? item.Quantity.ToString("N3", GermanCulture)
            : $"{item.Quantity.ToString("N3", GermanCulture)} {item.Unit}";

        table.Cell().Background(bgColor).Padding(5)
            .Text(displayPosition.ToString()).FontSize(9);
        table.Cell().Background(bgColor).Padding(5)
            .Text(text => AppendMultilineText(text, item.Description, 9));
        table.Cell().Background(bgColor).Padding(5).AlignRight()
            .Text(mengeText).FontSize(9);
        table.Cell().Background(bgColor).Padding(5).AlignRight()
            .Text(item.UnitPrice.ToString("C2", GermanCulture)).FontSize(9);
        table.Cell().Background(bgColor).Padding(5).AlignRight()
            .Text(item.TotalNet.ToString("C2", GermanCulture)).FontSize(9);
    }

    private void RenderItemRowWithBorder(TableDescriptor table, InvoiceItem item, string bgColor, int displayPosition)
    {
        var mengeText = string.IsNullOrWhiteSpace(item.Unit)
            ? item.Quantity.ToString("N3", GermanCulture)
            : $"{item.Quantity.ToString("N3", GermanCulture)} {item.Unit}";

        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(DividerColor).Padding(5)
            .Text(displayPosition.ToString()).FontSize(9);
        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(DividerColor).Padding(5)
            .Text(text => AppendMultilineText(text, item.Description, 9));
        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(DividerColor).Padding(5).AlignRight()
            .Text(mengeText).FontSize(9);
        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(DividerColor).Padding(5).AlignRight()
            .Text(item.UnitPrice.ToString("C2", GermanCulture)).FontSize(9);
        table.Cell().Background(bgColor).BorderBottom(1).BorderColor(DividerColor).Padding(5).AlignRight()
            .Text(item.TotalNet.ToString("C2", GermanCulture)).FontSize(9);
    }

    /// <summary>
    /// Renders the "Kleinunternehmer" notice if applicable.
    /// </summary>
    protected void RenderKleinunternehmerNotice(ColumnDescriptor column, Company company)
    {
        if (company.IsKleinunternehmer)
        {
            column.Item().PaddingTop(15).Text("Gemäß § 19 UStG wird keine Umsatzsteuer berechnet.")
                .FontSize(9)
                .Italic()
                .FontColor(TextSecondaryColor);
        }
    }

    protected void RenderReverseChargeNotice(ColumnDescriptor column, Invoice invoice, Company company)
    {
        if (!company.IsKleinunternehmer && invoice.IsReverseCharge)
        {
            column.Item().PaddingTop(15)
                .Text("Steuerschuldnerschaft des Leistungsempfängers gemäß § 13b UStG")
                .FontSize(9)
                .Italic()
                .FontColor(TextSecondaryColor);
        }
    }

    /// <summary>
    /// Renders the closing text.
    /// </summary>
    protected void RenderClosingText(ColumnDescriptor column, Invoice invoice, Company company, bool bold = false)
    {
        var footerText = invoice.Type == InvoiceType.CreditNote
            ? company.PdfCreditNoteFooterText
            : company.PdfFooterText;

        var text = !string.IsNullOrWhiteSpace(footerText)
            ? TemplateEngine.ReplacePlaceholders(footerText, invoice, company)
            : "Vielen Dank für Ihr Vertrauen!";

        var textStyle = column.Item().PaddingTop(10).Text(text).FontSize(10);
        if (bold) textStyle.Bold();
    }

    private static string? GetTaxIdentifierLine(Company company)
    {
        if (!string.IsNullOrWhiteSpace(company.VatId))
        {
            return $"USt-IdNr.: {company.VatId}";
        }

        if (!string.IsNullOrWhiteSpace(company.TaxNumber))
        {
            return $"Steuernr.: {company.TaxNumber}";
        }

        return null;
    }

    private static void AppendMultilineText(TextDescriptor text, string value, float fontSize)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var lines = value.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Length == 0 ? " " : lines[i];
            text.Line(line).FontSize(fontSize);
        }
    }
}
