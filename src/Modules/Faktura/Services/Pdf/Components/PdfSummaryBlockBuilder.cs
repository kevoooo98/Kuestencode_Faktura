using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace Kuestencode.Faktura.Services.Pdf.Components;

/// <summary>
/// Builds the summary block (totals, VAT, discounts, down payments) for PDF invoices.
/// </summary>
public class PdfSummaryBlockBuilder
{
    private readonly CultureInfo _germanCulture = new CultureInfo("de-DE");
    private const string TextSecondaryColor = "#6B7280";
    private const string DividerColor = "#E5E7EB";

    /// <summary>
    /// Renders the summary block with standard styling (no background color).
    /// </summary>
    public void RenderStandard(IContainer container, Invoice invoice, Company company)
    {
        container.AlignRight().Width(250).Column(sumColumn =>
        {
            RenderNetTotal(sumColumn, invoice);
            RenderDiscount(sumColumn, invoice);
            RenderVat(sumColumn, invoice, company);
            RenderGrossTotal(sumColumn, invoice, "#1A1A1A");
            RenderDownPayments(sumColumn, invoice);
        });
    }

    /// <summary>
    /// Renders the summary block with border (for Strukturiert layout).
    /// </summary>
    public void RenderWithBorder(IContainer container, Invoice invoice, Company company)
    {
        container.AlignRight().Width(250).Border(1).BorderColor(company.PdfAccentColor).Padding(10).Column(sumColumn =>
        {
            RenderNetTotal(sumColumn, invoice);
            RenderDiscount(sumColumn, invoice);
            RenderVat(sumColumn, invoice, company);
            RenderGrossTotal(sumColumn, invoice, "#1A1A1A");
            RenderDownPayments(sumColumn, invoice);
        });
    }

    /// <summary>
    /// Renders the summary block with colored background (for Betont layout).
    /// </summary>
    public void RenderWithBackground(IContainer container, Invoice invoice, Company company)
    {
        container.AlignRight().Width(250).Background(company.PdfAccentColor).Padding(10).Column(sumColumn =>
        {
            RenderNetTotal(sumColumn, invoice, "#FFFFFF");
            RenderDiscount(sumColumn, invoice, "#FFFFFF");
            RenderVat(sumColumn, invoice, company, "#FFFFFF");
            RenderGrossTotal(sumColumn, invoice, "#FFFFFF");
            RenderDownPayments(sumColumn, invoice, "#FFFFFF");
        });
    }

    private void RenderNetTotal(ColumnDescriptor sumColumn, Invoice invoice, string? textColor = null)
    {
        sumColumn.Item().Row(row =>
        {
            var text = row.RelativeItem().Text("Nettosumme:").FontSize(10);
            if (textColor != null) text.FontColor(textColor);

            var amountText = row.ConstantItem(100).AlignRight().Text(DisplayAmount(invoice, invoice.TotalNet).ToString("C2", _germanCulture)).FontSize(10);
            if (textColor != null) amountText.FontColor(textColor);
        });
    }

    private static decimal DisplayAmount(Invoice invoice, decimal amount) =>
        invoice.Type == InvoiceType.CreditNote ? Math.Abs(amount) : amount;

    private void RenderDiscount(ColumnDescriptor sumColumn, Invoice invoice, string? textColor = null)
    {
        if (invoice.DiscountAmount > 0)
        {
            sumColumn.Item().PaddingTop(3).Row(row =>
            {
                var discountText = invoice.DiscountType == DiscountType.Percentage
                    ? $"Rabatt ({invoice.DiscountValue}%):"
                    : "Rabatt:";

                var labelText = row.RelativeItem().Text(discountText).FontSize(10);
                if (textColor != null)
                    labelText.FontColor(textColor);
                else
                    labelText.FontColor(TextSecondaryColor);

                var amountText = row.ConstantItem(100).AlignRight().Text($"-{invoice.DiscountAmount.ToString("C2", _germanCulture)}").FontSize(10);
                if (textColor != null)
                    amountText.FontColor(textColor);
                else
                    amountText.FontColor(TextSecondaryColor);
            });

            sumColumn.Item().PaddingTop(3).Row(row =>
            {
                var text = row.RelativeItem().Text("Zwischensumme:").FontSize(10);
                if (textColor != null) text.FontColor(textColor);

                var amountText = row.ConstantItem(100).AlignRight().Text(DisplayAmount(invoice, invoice.TotalNetAfterDiscount).ToString("C2", _germanCulture)).FontSize(10);
                if (textColor != null) amountText.FontColor(textColor);
            });
        }
    }

    private void RenderVat(ColumnDescriptor sumColumn, Invoice invoice, Company company, string? textColor = null)
    {
        sumColumn.Item().PaddingTop(3).Row(row =>
        {
            var vatText = company.IsKleinunternehmer
                ? "MwSt (0% §19 UStG):"
                : invoice.IsReverseCharge
                    ? "MwSt (0%, §13b UStG):"
                    : $"MwSt ({invoice.Items.FirstOrDefault(i => !i.IsHeader)?.VatRate ?? 0}%):";

            var labelText = row.RelativeItem().Text(vatText).FontSize(10);
            if (textColor != null)
                labelText.FontColor(textColor);
            else
                labelText.FontColor(TextSecondaryColor);

            var amountText = row.ConstantItem(100).AlignRight().Text(DisplayAmount(invoice, invoice.TotalVat).ToString("C2", _germanCulture)).FontSize(10);
            if (textColor != null) amountText.FontColor(textColor);
        });
    }

    private void RenderGrossTotal(ColumnDescriptor sumColumn, Invoice invoice, string textColor)
    {
        var borderColor = textColor == "#FFFFFF" ? "#FFFFFF" : DividerColor;
        sumColumn.Item().PaddingTop(textColor == "#FFFFFF" ? 5 : 8)
            .BorderTop(textColor == "#FFFFFF" ? 2 : 1)
            .BorderColor(borderColor)
            .PaddingTop(5);

        var label = invoice.Type == InvoiceType.CreditNote ? "Gutschriftbetrag:" : "Bruttosumme:";

        sumColumn.Item().Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(textColor == "#FFFFFF" ? 12 : 11).FontColor(textColor);
            row.ConstantItem(100).AlignRight().Text(DisplayAmount(invoice, invoice.TotalGross).ToString("C2", _germanCulture)).FontSize(textColor == "#FFFFFF" ? 12 : 11).FontColor(textColor);
        });
    }

    private void RenderDownPayments(ColumnDescriptor sumColumn, Invoice invoice, string? textColor = null)
    {
        if (invoice.TotalDownPayments > 0)
        {
            // Aufschlüsselung je Abschlag siehe Abschlagsrechnungstabelle/Zahlungsübersicht (PdfDownPaymentDetailBuilder)
            var borderColor = textColor == "#FFFFFF" ? "#FFFFFF" : DividerColor;
            sumColumn.Item().PaddingTop(8).BorderTop(2).BorderColor(borderColor).PaddingTop(5);

            sumColumn.Item().Row(row =>
            {
                var labelStyle = row.RelativeItem().Text("Zu zahlen:").FontSize(textColor == "#FFFFFF" ? 13 : 12).Bold();
                if (textColor != null) labelStyle.FontColor(textColor);

                var amountStyle = row.ConstantItem(100).AlignRight().Text(invoice.AmountDue.ToString("C2", _germanCulture)).FontSize(textColor == "#FFFFFF" ? 13 : 12).Bold();
                if (textColor != null) amountStyle.FontColor(textColor);
            });
        }
    }
}
