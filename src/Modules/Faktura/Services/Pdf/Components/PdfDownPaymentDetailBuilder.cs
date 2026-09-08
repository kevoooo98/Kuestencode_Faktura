using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace Kuestencode.Faktura.Services.Pdf.Components;

/// <summary>
/// Rendert die detaillierte Abschlags-Aufstellung (Abschlagsrechnungstabelle + Zahlungsübersicht)
/// für Rechnungen mit Abschlägen.
/// </summary>
public class PdfDownPaymentDetailBuilder
{
    private readonly CultureInfo _germanCulture = new CultureInfo("de-DE");
    private const string TextSecondaryColor = "#6B7280";
    private const string DividerColor = "#E5E7EB";

    public void Render(IContainer container, Invoice invoice, Company company)
    {
        if (!invoice.DownPayments.Any())
            return;

        container.Column(column =>
        {
            column.Item().Element(c => RenderAbschlagsrechnungstabelle(c, invoice, company));

            if (invoice.DownPayments.Any(d => d.SourceInvoice != null))
            {
                column.Item().PaddingTop(15).Element(c => RenderZahlungsuebersicht(c, invoice, company));
            }
        });
    }

    private void RenderAbschlagsrechnungstabelle(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            column.Item().Text("Abschlagsrechnungstabelle").FontSize(11).Bold();

            column.Item().PaddingTop(5).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(2);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).Text("").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).Text("Rechnungsnummer").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("Entgelt").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("MwSt.").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("Summe").FontColor("#FFFFFF").FontSize(9).Bold();
                });

                AddDetailRow(table, "Gesamtrechnungsbetrag", string.Empty,
                    invoice.TotalNetAfterDiscount, invoice.TotalVat, invoice.TotalGross);

                var netSum = 0m;
                var vatSum = 0m;
                var grossSum = 0m;
                var index = 0;

                foreach (var downPayment in invoice.DownPayments)
                {
                    index++;
                    var (referenceNet, referenceGross) = ReferenceTotals(downPayment, invoice);
                    var (net, vat) = DownPaymentAllocationCalculator.Split(downPayment.Amount, referenceNet, referenceGross);
                    netSum += net;
                    vatSum += vat;
                    grossSum += downPayment.Amount;

                    var invoiceNumber = downPayment.SourceInvoice?.InvoiceNumber ?? downPayment.Description;
                    AddDetailRow(table, $"{index}. Abschlagsrechnung", invoiceNumber, -net, -vat, -downPayment.Amount);
                }

                table.Cell().ColumnSpan(5).PaddingTop(5).BorderTop(1).BorderColor(DividerColor);

                AddDetailRow(table, "Rechnungsbetrag", string.Empty,
                    invoice.TotalNetAfterDiscount - netSum, invoice.TotalVat - vatSum, invoice.TotalGross - grossSum,
                    bold: true);
            });
        });
    }

    private void RenderZahlungsuebersicht(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            column.Item().Text("Zahlungsübersicht").FontSize(11).Bold();

            column.Item().PaddingTop(5).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(4);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).Text("").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("Entgelt").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("MwSt.").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("Summe").FontColor("#FFFFFF").FontSize(9).Bold();
                });

                AddSummaryRow(table, "Gesamtrechnungsbetrag", invoice.TotalNetAfterDiscount, invoice.TotalVat, invoice.TotalGross);

                var netSum = 0m;
                var vatSum = 0m;
                var grossSum = 0m;

                foreach (var downPayment in invoice.DownPayments.Where(d => d.SourceInvoice != null))
                {
                    var sourceInvoice = downPayment.SourceInvoice!;
                    var (referenceNet, referenceGross) = (sourceInvoice.TotalNetAfterDiscount, sourceInvoice.TotalGross);

                    var paymentIndex = 0;
                    foreach (var payment in sourceInvoice.Payments.OrderBy(p => p.PaymentDate))
                    {
                        paymentIndex++;
                        var (net, vat) = DownPaymentAllocationCalculator.Split(payment.Amount, referenceNet, referenceGross);
                        netSum += net;
                        vatSum += vat;
                        grossSum += payment.Amount;

                        AddSummaryRow(table, $"{paymentIndex}. Zahlung zu {sourceInvoice.InvoiceNumber}", -net, -vat, -payment.Amount);
                    }
                }

                table.Cell().ColumnSpan(4).PaddingTop(5).BorderTop(1).BorderColor(DividerColor);

                AddSummaryRow(table, "Verbleibender Betrag",
                    invoice.TotalNetAfterDiscount - netSum, invoice.TotalVat - vatSum, invoice.TotalGross - grossSum,
                    bold: true);
            });
        });
    }

    /// <summary>
    /// Ermittelt die Netto/Brutto-Referenzwerte für die MwSt-Aufteilung eines Abschlags:
    /// bevorzugt die verknüpfte Abschlagsrechnung, sonst die aktuelle Rechnung selbst.
    /// </summary>
    private static (decimal Net, decimal Gross) ReferenceTotals(DownPayment downPayment, Invoice invoice)
    {
        return downPayment.SourceInvoice != null
            ? (downPayment.SourceInvoice.TotalNetAfterDiscount, downPayment.SourceInvoice.TotalGross)
            : (invoice.TotalNetAfterDiscount, invoice.TotalGross);
    }

    private void AddDetailRow(TableDescriptor table, string label, string invoiceNumber, decimal net, decimal vat, decimal gross, bool bold = false)
    {
        var textColor = bold ? "#1A1A1A" : TextSecondaryColor;

        AddCell(table.Cell().Padding(5), label, textColor, bold);
        AddCell(table.Cell().Padding(5), invoiceNumber, textColor, bold);
        AddCell(table.Cell().Padding(5).AlignRight(), net.ToString("C2", _germanCulture), textColor, bold);
        AddCell(table.Cell().Padding(5).AlignRight(), vat.ToString("C2", _germanCulture), textColor, bold);
        AddCell(table.Cell().Padding(5).AlignRight(), gross.ToString("C2", _germanCulture), textColor, bold);
    }

    private void AddSummaryRow(TableDescriptor table, string label, decimal net, decimal vat, decimal gross, bool bold = false)
    {
        var textColor = bold ? "#1A1A1A" : TextSecondaryColor;

        AddCell(table.Cell().Padding(5), label, textColor, bold);
        AddCell(table.Cell().Padding(5).AlignRight(), net.ToString("C2", _germanCulture), textColor, bold);
        AddCell(table.Cell().Padding(5).AlignRight(), vat.ToString("C2", _germanCulture), textColor, bold);
        AddCell(table.Cell().Padding(5).AlignRight(), gross.ToString("C2", _germanCulture), textColor, bold);
    }

    private static void AddCell(IContainer cell, string text, string textColor, bool bold)
    {
        var span = cell.Text(text).FontSize(9).FontColor(textColor);
        if (bold) span.Bold();
    }
}
