using InvoiceApp.Data;
using InvoiceApp.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using QRCoder;
using SkiaSharp;

namespace InvoiceApp.Services;

public class PdfGeneratorService : IPdfGeneratorService
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly CultureInfo _germanCulture = new CultureInfo("de-DE");

    // Küstencode Farbpalette
    private static class Colors
    {
        public static string Primary = "#0F2A3D";
        public static string TextPrimary = "#1A1A1A";
        public static string TextSecondary = "#6B7280";
        public static string Background = "#F4F6F8";
        public static string Divider = "#E5E7EB";
    }

    public PdfGeneratorService(ApplicationDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;

        // QuestPDF Lizenz-Konfiguration für Community-Nutzung
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateInvoicePdf(int invoiceId)
    {
        var invoice = _context.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Items)
            .Include(i => i.DownPayments)
            .FirstOrDefault(i => i.Id == invoiceId);

        if (invoice == null)
        {
            throw new InvalidOperationException("Rechnung nicht gefunden");
        }

        var company = _context.Companies.FirstOrDefault();
        if (company == null)
        {
            throw new InvalidOperationException("Firmendaten nicht gefunden");
        }

        return GeneratePdfWithCompany(invoice, company);
    }

    public byte[] GeneratePdfWithCompany(Invoice invoice, Company company)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.TextPrimary));

                // Select layout based on company settings
                switch (company.PdfLayout)
                {
                    case PdfLayout.Klar:
                        page.Header().Element(c => ComposeHeaderKlar(c, invoice, company));
                        page.Content().Element(c => ComposeContentKlar(c, invoice, company));
                        page.Footer().Element(c => ComposeFooter(c, company));
                        break;
                    case PdfLayout.Strukturiert:
                        page.Header().Element(c => ComposeHeaderStrukturiert(c, invoice, company));
                        page.Content().Element(c => ComposeContentStrukturiert(c, invoice, company));
                        page.Footer().Element(c => ComposeFooter(c, company));
                        break;
                    case PdfLayout.Betont:
                        page.Header().Element(c => ComposeHeaderBetont(c, invoice, company));
                        page.Content().Element(c => ComposeContentBetont(c, invoice, company));
                        page.Footer().Element(c => ComposeFooter(c, company));
                        break;
                    default:
                        // Fallback to Klar
                        page.Header().Element(c => ComposeHeaderKlar(c, invoice, company));
                        page.Content().Element(c => ComposeContentKlar(c, invoice, company));
                        page.Footer().Element(c => ComposeFooter(c, company));
                        break;
                }
            });
        });

        return document.GeneratePdf();
    }

    public async Task<string> GenerateAndSaveAsync(int invoiceId)
    {
        var invoice = await _context.Invoices.FindAsync(invoiceId);
        if (invoice == null)
        {
            throw new InvalidOperationException("Rechnung nicht gefunden");
        }

        var pdfBytes = GenerateInvoicePdf(invoiceId);

        var invoicesPath = Path.Combine(_environment.WebRootPath, "invoices");
        Directory.CreateDirectory(invoicesPath);

        var fileName = $"{invoice.InvoiceNumber}.pdf";
        var filePath = Path.Combine(invoicesPath, fileName);

        await File.WriteAllBytesAsync(filePath, pdfBytes);

        return fileName;
    }

    private void ComposeHeaderKlar(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                // Linke Seite: Firmendaten
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
                            .FontColor(Colors.TextSecondary);
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
                        .FontColor(Colors.TextSecondary);
                    leftColumn.Item().Text($"{company.PostalCode} {company.City}")
                        .FontSize(9)
                        .FontColor(Colors.TextSecondary);

                    if (!string.IsNullOrEmpty(company.Email))
                    {
                        leftColumn.Item().PaddingTop(3).Text(company.Email)
                            .FontSize(9)
                            .FontColor(Colors.TextSecondary);
                    }
                    if (!string.IsNullOrEmpty(company.Phone))
                    {
                        leftColumn.Item().Text(company.Phone)
                            .FontSize(9)
                            .FontColor(Colors.TextSecondary);
                    }
                });

                // Rechte Seite: Logo (falls vorhanden) + Rechnungsmetadaten
                row.RelativeItem().AlignRight().Column(rightColumn =>
                {
                    // Logo anzeigen falls vorhanden
                    if (company.LogoData != null && company.LogoData.Length > 0)
                    {
                        rightColumn.Item().MaxWidth(150).Image(company.LogoData);
                        rightColumn.Item().PaddingBottom(10);
                    }

                    rightColumn.Item().Text($"Rechnung {invoice.InvoiceNumber}")
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
                            .FontColor(Colors.TextSecondary);
                        rightColumn.Item().Text($"{invoice.ServicePeriodStart.Value:dd.MM.yyyy} - {invoice.ServicePeriodEnd.Value:dd.MM.yyyy}")
                            .FontSize(9);
                    }
                });
            });

            // Trennlinie
            column.Item().PaddingTop(15).PaddingBottom(10)
                .BorderBottom(1)
                .BorderColor(Colors.Divider);
        });
    }

    private void ComposeContentKlar(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            // Empfängeradresse
            column.Item().PaddingTop(20).Column(addressColumn =>
            {
                if (invoice.Customer != null)
                {
                    addressColumn.Item().Text(invoice.Customer.Name)
                        .FontSize(11)
                        .Bold();
                    addressColumn.Item().Text(invoice.Customer.Address)
                        .FontSize(10);
                    addressColumn.Item().Text($"{invoice.Customer.PostalCode} {invoice.Customer.City}")
                        .FontSize(10);
                }
            });

            // Anrede und Einleitungstext
            if (!string.IsNullOrWhiteSpace(company.PdfHeaderText))
            {
                column.Item().PaddingTop(30).Text(ReplacePdfPlaceholders(company.PdfHeaderText, invoice, company))
                    .FontSize(10);
            }
            else
            {
                column.Item().PaddingTop(30).Column(greetingColumn =>
                {
                    greetingColumn.Item().Text("Sehr geehrte Damen und Herren,")
                        .FontSize(10);

                    greetingColumn.Item().PaddingTop(10).Text("hiermit stellen wir Ihnen folgende Leistungen in Rechnung:")
                        .FontSize(10);
                });
            }

            // Positionstabelle
            column.Item().PaddingTop(20).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(40);  // Pos.
                    columns.RelativeColumn(4);   // Beschreibung
                    columns.ConstantColumn(60);  // Menge
                    columns.ConstantColumn(80);  // Einzelpreis
                    columns.ConstantColumn(80);  // Gesamtpreis
                });

                // Header
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

                // Positionen
                var orderedItems = invoice.Items.OrderBy(i => i.Position).ToList();
                for (int i = 0; i < orderedItems.Count; i++)
                {
                    var item = orderedItems[i];
                    var bgColor = i % 2 == 0 ? "#FFFFFF" : Colors.Background;

                    table.Cell().Background(bgColor).Padding(5)
                        .Text(item.Position.ToString()).FontSize(9);
                    table.Cell().Background(bgColor).Padding(5)
                        .Text(item.Description).FontSize(9);
                    table.Cell().Background(bgColor).Padding(5).AlignRight()
                        .Text(item.Quantity.ToString("N3", _germanCulture)).FontSize(9);
                    table.Cell().Background(bgColor).Padding(5).AlignRight()
                        .Text(item.UnitPrice.ToString("C2", _germanCulture)).FontSize(9);
                    table.Cell().Background(bgColor).Padding(5).AlignRight()
                        .Text(item.TotalNet.ToString("C2", _germanCulture)).FontSize(9);
                }
            });

            // Summenblock
            column.Item().PaddingTop(15).AlignRight().Width(250).Column(sumColumn =>
            {
                sumColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text("Nettosumme:").FontSize(10);
                    row.ConstantItem(100).AlignRight().Text(invoice.TotalNet.ToString("C2", _germanCulture)).FontSize(10);
                });

                // Rabatt anzeigen, falls vorhanden
                if (invoice.DiscountAmount > 0)
                {
                    sumColumn.Item().PaddingTop(3).Row(row =>
                    {
                        var discountText = invoice.DiscountType == DiscountType.Percentage
                            ? $"Rabatt ({invoice.DiscountValue}%):"
                            : "Rabatt:";
                        row.RelativeItem().Text(discountText).FontSize(10).FontColor(Colors.TextSecondary);
                        row.ConstantItem(100).AlignRight().Text($"-{invoice.DiscountAmount.ToString("C2", _germanCulture)}").FontSize(10).FontColor(Colors.TextSecondary);
                    });

                    sumColumn.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text("Zwischensumme:").FontSize(10);
                        row.ConstantItem(100).AlignRight().Text(invoice.TotalNetAfterDiscount.ToString("C2", _germanCulture)).FontSize(10);
                    });
                }

                sumColumn.Item().PaddingTop(3).Row(row =>
                {
                    var vatText = company.IsKleinunternehmer
                        ? "MwSt (0% §19 UStG):"
                        : $"MwSt ({invoice.Items.FirstOrDefault()?.VatRate ?? 0}%):";
                    row.RelativeItem().Text(vatText).FontSize(10).FontColor(Colors.TextSecondary);
                    row.ConstantItem(100).AlignRight().Text(invoice.TotalVat.ToString("C2", _germanCulture)).FontSize(10);
                });

                sumColumn.Item().PaddingTop(8).BorderTop(1).BorderColor(Colors.Divider).PaddingTop(5);

                sumColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text("Bruttosumme:").FontSize(11);
                    row.ConstantItem(100).AlignRight().Text(invoice.TotalGross.ToString("C2", _germanCulture)).FontSize(11);
                });

                // Abschläge anzeigen, falls vorhanden
                if (invoice.TotalDownPayments > 0)
                {
                    // Abschläge auflisten
                    sumColumn.Item().PaddingTop(8).Text("Abgezogen:").FontSize(9).FontColor(Colors.TextSecondary);

                    foreach (var downPayment in invoice.DownPayments)
                    {
                        sumColumn.Item().PaddingTop(2).Row(row =>
                        {
                            var dateText = downPayment.PaymentDate.HasValue
                                ? $"{downPayment.Description} ({downPayment.PaymentDate.Value:dd.MM.yyyy})"
                                : downPayment.Description;
                            row.RelativeItem().Text(dateText).FontSize(9).FontColor(Colors.TextSecondary);
                            row.ConstantItem(100).AlignRight().Text($"-{downPayment.Amount.ToString("C2", _germanCulture)}").FontSize(9).FontColor(Colors.TextSecondary);
                        });
                    }

                    sumColumn.Item().PaddingTop(8).BorderTop(2).BorderColor(Colors.Divider).PaddingTop(5);

                    sumColumn.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Zu zahlen:").FontSize(12).Bold();
                        row.ConstantItem(100).AlignRight().Text(invoice.AmountDue.ToString("C2", _germanCulture)).FontSize(12).Bold();
                    });
                }
            });

            // Kleinunternehmer-Hinweis
            if (company.IsKleinunternehmer)
            {
                column.Item().PaddingTop(15).Text("Gemäß § 19 UStG wird keine Umsatzsteuer berechnet.")
                    .FontSize(9)
                    .Italic()
                    .FontColor(Colors.TextSecondary);
            }

            // Zahlungsinformationen mit QR-Code
            column.Item().PaddingTop(20).Row(paymentRow =>
            {
                // Linke Seite: Zahlungsinformationen
                paymentRow.RelativeItem().Column(paymentColumn =>
                {
                    if (!string.IsNullOrWhiteSpace(company.PdfPaymentNotice))
                    {
                        paymentColumn.Item().Text(ReplacePdfPlaceholders(company.PdfPaymentNotice, invoice, company)).FontSize(10);
                    }
                    else
                    {
                        var dueText = invoice.DueDate.HasValue
                            ? $"Bitte überweisen Sie den Betrag bis zum {invoice.DueDate.Value:dd.MM.yyyy} auf folgendes Konto:"
                            : "Bitte überweisen Sie den Betrag auf folgendes Konto:";

                        paymentColumn.Item().Text(dueText).FontSize(10);
                    }

                    paymentColumn.Item().PaddingTop(8).Column(bankColumn =>
                    {
                        bankColumn.Item().Text($"Bankname: {company.BankName}").FontSize(9);
                        bankColumn.Item().Text($"IBAN: {company.BankAccount}").FontSize(9);

                        if (!string.IsNullOrEmpty(company.Bic))
                        {
                            bankColumn.Item().Text($"BIC: {company.Bic}").FontSize(9);
                        }

                        var accountHolder = !string.IsNullOrWhiteSpace(company.AccountHolder)
                            ? company.AccountHolder
                            : company.OwnerFullName;
                        bankColumn.Item().Text($"Kontoinhaber: {accountHolder}").FontSize(9);

                        bankColumn.Item().PaddingTop(3).Text($"Verwendungszweck: {invoice.InvoiceNumber}").FontSize(9).Bold();
                    });
                });

                // Rechte Seite: QR-Code
                paymentRow.ConstantItem(100).AlignRight().Column(qrColumn =>
                {
                    var qrCodeBytes = GenerateGiroCodeQR(invoice, company);
                    qrColumn.Item().Width(80).Height(80).Image(qrCodeBytes);
                    qrColumn.Item().PaddingTop(3).Text("QR-Code für Überweisung")
                        .FontSize(7)
                        .FontColor(Colors.TextSecondary)
                        .AlignCenter();
                });
            });

            // Abschlusstext
            if (!string.IsNullOrWhiteSpace(company.PdfFooterText))
            {
                column.Item().PaddingTop(20).Text(ReplacePdfPlaceholders(company.PdfFooterText, invoice, company))
                    .FontSize(10);
            }
            else
            {
                column.Item().PaddingTop(20).Text("Vielen Dank für Ihr Vertrauen!")
                    .FontSize(10);
            }
        });
    }

    // Layout: Strukturiert - Mit Boxen und Trennlinien
    private void ComposeHeaderStrukturiert(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            // Header mit farbigem Hintergrund
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

            // Rechnungsinfo Box
            column.Item().PaddingTop(10).Border(1).BorderColor(company.PdfAccentColor).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(leftCol =>
                {
                    leftCol.Item().Text($"Rechnung {invoice.InvoiceNumber}")
                        .FontSize(14)
                        .Bold()
                        .FontColor(company.PdfPrimaryColor);
                    leftCol.Item().Text($"Datum: {invoice.InvoiceDate:dd.MM.yyyy}").FontSize(10);
                    if (invoice.Customer != null)
                    {
                        leftCol.Item().Text($"Kundennr.: {invoice.Customer.CustomerNumber}").FontSize(10);
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

    private void ComposeContentStrukturiert(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            // Empfängeradresse in Box
            column.Item().PaddingTop(15).Border(1).BorderColor(Colors.Divider).Padding(10).Column(addressColumn =>
            {
                if (invoice.Customer != null)
                {
                    addressColumn.Item().Text(invoice.Customer.Name).FontSize(11).Bold();
                    addressColumn.Item().Text(invoice.Customer.Address).FontSize(10);
                    addressColumn.Item().Text($"{invoice.Customer.PostalCode} {invoice.Customer.City}").FontSize(10);
                }
            });

            // Anrede
            if (!string.IsNullOrWhiteSpace(company.PdfHeaderText))
            {
                column.Item().PaddingTop(20).Text(ReplacePdfPlaceholders(company.PdfHeaderText, invoice, company)).FontSize(10);
            }
            else
            {
                column.Item().PaddingTop(20).Text("Sehr geehrte Damen und Herren,").FontSize(10);
                column.Item().PaddingTop(10).Text("hiermit stellen wir Ihnen folgende Leistungen in Rechnung:").FontSize(10);
            }

            // Positionstabelle mit stärkerer Strukturierung
            column.Item().PaddingTop(20).Border(1).BorderColor(company.PdfPrimaryColor).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(40);
                    columns.RelativeColumn(4);
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).Text("Pos.").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).Text("Beschreibung").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("Menge").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("Einzelpreis").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("Gesamtpreis").FontColor("#FFFFFF").FontSize(9).Bold();
                });

                var orderedItems = invoice.Items.OrderBy(i => i.Position).ToList();
                for (int i = 0; i < orderedItems.Count; i++)
                {
                    var item = orderedItems[i];
                    var bgColor = i % 2 == 0 ? "#FFFFFF" : Colors.Background;

                    table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Divider).Padding(5).Text(item.Position.ToString()).FontSize(9);
                    table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Divider).Padding(5).Text(item.Description).FontSize(9);
                    table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Divider).Padding(5).AlignRight().Text(item.Quantity.ToString("N3", _germanCulture)).FontSize(9);
                    table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Divider).Padding(5).AlignRight().Text(item.UnitPrice.ToString("C2", _germanCulture)).FontSize(9);
                    table.Cell().Background(bgColor).BorderBottom(1).BorderColor(Colors.Divider).Padding(5).AlignRight().Text(item.TotalNet.ToString("C2", _germanCulture)).FontSize(9);
                }
            });

            // Summenblock in Box
            column.Item().PaddingTop(15).AlignRight().Width(250).Border(1).BorderColor(company.PdfAccentColor).Padding(10).Column(sumColumn =>
            {
                sumColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text("Nettosumme:").FontSize(10);
                    row.ConstantItem(100).AlignRight().Text(invoice.TotalNet.ToString("C2", _germanCulture)).FontSize(10);
                });

                // Rabatt anzeigen, falls vorhanden
                if (invoice.DiscountAmount > 0)
                {
                    sumColumn.Item().PaddingTop(3).Row(row =>
                    {
                        var discountText = invoice.DiscountType == DiscountType.Percentage
                            ? $"Rabatt ({invoice.DiscountValue}%):"
                            : "Rabatt:";
                        row.RelativeItem().Text(discountText).FontSize(10).FontColor(Colors.TextSecondary);
                        row.ConstantItem(100).AlignRight().Text($"-{invoice.DiscountAmount.ToString("C2", _germanCulture)}").FontSize(10).FontColor(Colors.TextSecondary);
                    });

                    sumColumn.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text("Zwischensumme:").FontSize(10);
                        row.ConstantItem(100).AlignRight().Text(invoice.TotalNetAfterDiscount.ToString("C2", _germanCulture)).FontSize(10);
                    });
                }

                sumColumn.Item().PaddingTop(3).Row(row =>
                {
                    var vatText = company.IsKleinunternehmer ? "MwSt (0% §19 UStG):" : $"MwSt ({invoice.Items.FirstOrDefault()?.VatRate ?? 0}%):";
                    row.RelativeItem().Text(vatText).FontSize(10);
                    row.ConstantItem(100).AlignRight().Text(invoice.TotalVat.ToString("C2", _germanCulture)).FontSize(10);
                });

                sumColumn.Item().PaddingTop(5).BorderTop(1).BorderColor(Colors.Divider).PaddingTop(5);

                sumColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text("Bruttosumme:").FontSize(11);
                    row.ConstantItem(100).AlignRight().Text(invoice.TotalGross.ToString("C2", _germanCulture)).FontSize(11);
                });

                // Abschläge anzeigen, falls vorhanden
                if (invoice.TotalDownPayments > 0)
                {
                    sumColumn.Item().PaddingTop(8).Text("Abgezogen:").FontSize(9).FontColor(Colors.TextSecondary);

                    foreach (var downPayment in invoice.DownPayments)
                    {
                        sumColumn.Item().PaddingTop(2).Row(row =>
                        {
                            var dateText = downPayment.PaymentDate.HasValue
                                ? $"{downPayment.Description} ({downPayment.PaymentDate.Value:dd.MM.yyyy})"
                                : downPayment.Description;
                            row.RelativeItem().Text(dateText).FontSize(9).FontColor(Colors.TextSecondary);
                            row.ConstantItem(100).AlignRight().Text($"-{downPayment.Amount.ToString("C2", _germanCulture)}").FontSize(9).FontColor(Colors.TextSecondary);
                        });
                    }

                    sumColumn.Item().PaddingTop(8).BorderTop(2).BorderColor(Colors.Divider).PaddingTop(5);

                    sumColumn.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Zu zahlen:").FontSize(12).Bold();
                        row.ConstantItem(100).AlignRight().Text(invoice.AmountDue.ToString("C2", _germanCulture)).FontSize(12).Bold();
                    });
                }
            });

            if (company.IsKleinunternehmer)
            {
                column.Item().PaddingTop(15).Text("Gemäß § 19 UStG wird keine Umsatzsteuer berechnet.").FontSize(9).Italic().FontColor(Colors.TextSecondary);
            }

            // Zahlungsinformationen mit QR-Code
            column.Item().PaddingTop(20).Row(paymentRow =>
            {
                // Linke Seite: Zahlungsinformationen
                paymentRow.RelativeItem().Column(paymentColumn =>
                {
                    if (!string.IsNullOrWhiteSpace(company.PdfPaymentNotice))
                    {
                        paymentColumn.Item().Text(ReplacePdfPlaceholders(company.PdfPaymentNotice, invoice, company)).FontSize(10);
                    }
                    else
                    {
                        var dueText = invoice.DueDate.HasValue
                            ? $"Bitte überweisen Sie den Betrag bis zum {invoice.DueDate.Value:dd.MM.yyyy} auf folgendes Konto:"
                            : "Bitte überweisen Sie den Betrag auf folgendes Konto:";
                        paymentColumn.Item().Text(dueText).FontSize(10);
                    }

                    paymentColumn.Item().PaddingTop(8).Column(bankColumn =>
                    {
                        bankColumn.Item().Text($"Bankname: {company.BankName}").FontSize(9);
                        bankColumn.Item().Text($"IBAN: {company.BankAccount}").FontSize(9);
                        if (!string.IsNullOrEmpty(company.Bic))
                        {
                            bankColumn.Item().Text($"BIC: {company.Bic}").FontSize(9);
                        }
                        var accountHolder = !string.IsNullOrWhiteSpace(company.AccountHolder) ? company.AccountHolder : company.OwnerFullName;
                        bankColumn.Item().Text($"Kontoinhaber: {accountHolder}").FontSize(9);
                        bankColumn.Item().PaddingTop(3).Text($"Verwendungszweck: {invoice.InvoiceNumber}").FontSize(9).Bold();
                    });
                });

                // Rechte Seite: QR-Code
                paymentRow.ConstantItem(100).AlignRight().Column(qrColumn =>
                {
                    var qrCodeBytes = GenerateGiroCodeQR(invoice, company);
                    qrColumn.Item().Width(80).Height(80).Image(qrCodeBytes);
                    qrColumn.Item().PaddingTop(3).Text("QR-Code für Überweisung")
                        .FontSize(7)
                        .FontColor(Colors.TextSecondary)
                        .AlignCenter();
                });
            });

            if (!string.IsNullOrWhiteSpace(company.PdfFooterText))
            {
                column.Item().PaddingTop(20).Text(ReplacePdfPlaceholders(company.PdfFooterText, invoice, company)).FontSize(10);
            }
            else
            {
                column.Item().PaddingTop(20).Text("Vielen Dank für Ihr Vertrauen!").FontSize(10);
            }
        });
    }

    // Layout: Betont - Farbiger Header, visuell stärker
    private void ComposeHeaderBetont(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            // Großer farbiger Header
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

                // Rechnungsnummer hervorgehoben
                headerCol.Item().PaddingTop(15).Background(company.PdfAccentColor).Padding(10).Text($"RECHNUNG {invoice.InvoiceNumber}")
                    .FontSize(16)
                    .Bold()
                    .FontColor("#FFFFFF")
                    .AlignCenter();
            });

            // Metadaten-Box
            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(leftCol =>
                {
                    leftCol.Item().Text($"Datum: {invoice.InvoiceDate:dd.MM.yyyy}").FontSize(10);
                    if (invoice.Customer != null)
                    {
                        leftCol.Item().Text($"Kundennr.: {invoice.Customer.CustomerNumber}").FontSize(10);
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

    private void ComposeContentBetont(IContainer container, Invoice invoice, Company company)
    {
        container.Column(column =>
        {
            // Empfängeradresse mit Akzent
            column.Item().PaddingTop(15).BorderLeft(3).BorderColor(company.PdfAccentColor).PaddingLeft(10).Column(addressColumn =>
            {
                if (invoice.Customer != null)
                {
                    addressColumn.Item().Text(invoice.Customer.Name).FontSize(11).Bold().FontColor(company.PdfPrimaryColor);
                    addressColumn.Item().Text(invoice.Customer.Address).FontSize(10);
                    addressColumn.Item().Text($"{invoice.Customer.PostalCode} {invoice.Customer.City}").FontSize(10);
                }
            });

            // Anrede
            if (!string.IsNullOrWhiteSpace(company.PdfHeaderText))
            {
                column.Item().PaddingTop(20).Text(ReplacePdfPlaceholders(company.PdfHeaderText, invoice, company)).FontSize(10);
            }
            else
            {
                column.Item().PaddingTop(20).Text("Sehr geehrte Damen und Herren,").FontSize(10);
                column.Item().PaddingTop(10).Text("hiermit stellen wir Ihnen folgende Leistungen in Rechnung:").FontSize(10);
            }

            // Positionstabelle
            column.Item().PaddingTop(20).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(40);
                    columns.RelativeColumn(4);
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).Text("Pos.").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).Text("Beschreibung").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("Menge").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("Einzelpreis").FontColor("#FFFFFF").FontSize(9).Bold();
                    header.Cell().Background(company.PdfPrimaryColor).Padding(5).AlignRight().Text("Gesamtpreis").FontColor("#FFFFFF").FontSize(9).Bold();
                });

                var orderedItems = invoice.Items.OrderBy(i => i.Position).ToList();
                for (int i = 0; i < orderedItems.Count; i++)
                {
                    var item = orderedItems[i];
                    var bgColor = i % 2 == 0 ? "#FFFFFF" : Colors.Background;

                    table.Cell().Background(bgColor).Padding(5).Text(item.Position.ToString()).FontSize(9);
                    table.Cell().Background(bgColor).Padding(5).Text(item.Description).FontSize(9);
                    table.Cell().Background(bgColor).Padding(5).AlignRight().Text(item.Quantity.ToString("N3", _germanCulture)).FontSize(9);
                    table.Cell().Background(bgColor).Padding(5).AlignRight().Text(item.UnitPrice.ToString("C2", _germanCulture)).FontSize(9);
                    table.Cell().Background(bgColor).Padding(5).AlignRight().Text(item.TotalNet.ToString("C2", _germanCulture)).FontSize(9);
                }
            });

            // Summenblock hervorgehoben
            column.Item().PaddingTop(15).AlignRight().Width(250).Background(company.PdfAccentColor).Padding(10).Column(sumColumn =>
            {
                sumColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text("Nettosumme:").FontSize(10).FontColor("#FFFFFF");
                    row.ConstantItem(100).AlignRight().Text(invoice.TotalNet.ToString("C2", _germanCulture)).FontSize(10).FontColor("#FFFFFF");
                });

                // Rabatt anzeigen, falls vorhanden
                if (invoice.DiscountAmount > 0)
                {
                    sumColumn.Item().PaddingTop(3).Row(row =>
                    {
                        var discountText = invoice.DiscountType == DiscountType.Percentage
                            ? $"Rabatt ({invoice.DiscountValue}%):"
                            : "Rabatt:";
                        row.RelativeItem().Text(discountText).FontSize(10).FontColor("#FFFFFF").Opacity(0.8);
                        row.ConstantItem(100).AlignRight().Text($"-{invoice.DiscountAmount.ToString("C2", _germanCulture)}").FontSize(10).FontColor("#FFFFFF").Opacity(0.8);
                    });

                    sumColumn.Item().PaddingTop(3).Row(row =>
                    {
                        row.RelativeItem().Text("Zwischensumme:").FontSize(10).FontColor("#FFFFFF");
                        row.ConstantItem(100).AlignRight().Text(invoice.TotalNetAfterDiscount.ToString("C2", _germanCulture)).FontSize(10).FontColor("#FFFFFF");
                    });
                }

                sumColumn.Item().PaddingTop(3).Row(row =>
                {
                    var vatText = company.IsKleinunternehmer ? "MwSt (0% §19 UStG):" : $"MwSt ({invoice.Items.FirstOrDefault()?.VatRate ?? 0}%):";
                    row.RelativeItem().Text(vatText).FontSize(10).FontColor("#FFFFFF");
                    row.ConstantItem(100).AlignRight().Text(invoice.TotalVat.ToString("C2", _germanCulture)).FontSize(10).FontColor("#FFFFFF");
                });

                sumColumn.Item().PaddingTop(5).BorderTop(2).BorderColor("#FFFFFF").PaddingTop(5);

                sumColumn.Item().Row(row =>
                {
                    row.RelativeItem().Text("Bruttosumme:").FontSize(12).FontColor("#FFFFFF");
                    row.ConstantItem(100).AlignRight().Text(invoice.TotalGross.ToString("C2", _germanCulture)).FontSize(12).FontColor("#FFFFFF");
                });

                // Abschläge anzeigen, falls vorhanden
                if (invoice.TotalDownPayments > 0)
                {
                    sumColumn.Item().PaddingTop(8).Text("Abgezogen:").FontSize(9).FontColor("#FFFFFF").Opacity(0.8);

                    foreach (var downPayment in invoice.DownPayments)
                    {
                        sumColumn.Item().PaddingTop(2).Row(row =>
                        {
                            var dateText = downPayment.PaymentDate.HasValue
                                ? $"{downPayment.Description} ({downPayment.PaymentDate.Value:dd.MM.yyyy})"
                                : downPayment.Description;
                            row.RelativeItem().Text(dateText).FontSize(9).FontColor("#FFFFFF").Opacity(0.8);
                            row.ConstantItem(100).AlignRight().Text($"-{downPayment.Amount.ToString("C2", _germanCulture)}").FontSize(9).FontColor("#FFFFFF").Opacity(0.8);
                        });
                    }

                    sumColumn.Item().PaddingTop(8).BorderTop(2).BorderColor("#FFFFFF").PaddingTop(5);

                    sumColumn.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Zu zahlen:").FontSize(13).Bold().FontColor("#FFFFFF");
                        row.ConstantItem(100).AlignRight().Text(invoice.AmountDue.ToString("C2", _germanCulture)).FontSize(13).Bold().FontColor("#FFFFFF");
                    });
                }
            });

            if (company.IsKleinunternehmer)
            {
                column.Item().PaddingTop(15).Text("Gemäß § 19 UStG wird keine Umsatzsteuer berechnet.").FontSize(9).Italic().FontColor(Colors.TextSecondary);
            }

            // Zahlungsinformationen mit QR-Code
            column.Item().PaddingTop(20).Row(paymentRow =>
            {
                // Linke Seite: Zahlungsinformationen
                paymentRow.RelativeItem().Column(paymentColumn =>
                {
                    if (!string.IsNullOrWhiteSpace(company.PdfPaymentNotice))
                    {
                        paymentColumn.Item().Text(ReplacePdfPlaceholders(company.PdfPaymentNotice, invoice, company)).FontSize(10).Bold();
                    }
                    else
                    {
                        var dueText = invoice.DueDate.HasValue
                            ? $"Bitte überweisen Sie den Betrag bis zum {invoice.DueDate.Value:dd.MM.yyyy} auf folgendes Konto:"
                            : "Bitte überweisen Sie den Betrag auf folgendes Konto:";
                        paymentColumn.Item().Text(dueText).FontSize(10).Bold();
                    }

                    paymentColumn.Item().PaddingTop(8).Column(bankColumn =>
                    {
                        bankColumn.Item().Text($"Bankname: {company.BankName}").FontSize(9);
                        bankColumn.Item().Text($"IBAN: {company.BankAccount}").FontSize(9);
                        if (!string.IsNullOrEmpty(company.Bic))
                        {
                            bankColumn.Item().Text($"BIC: {company.Bic}").FontSize(9);
                        }
                        var accountHolder = !string.IsNullOrWhiteSpace(company.AccountHolder) ? company.AccountHolder : company.OwnerFullName;
                        bankColumn.Item().Text($"Kontoinhaber: {accountHolder}").FontSize(9);
                        bankColumn.Item().PaddingTop(3).Text($"Verwendungszweck: {invoice.InvoiceNumber}").FontSize(9).Bold();
                    });
                });

                // Rechte Seite: QR-Code
                paymentRow.ConstantItem(100).AlignRight().Column(qrColumn =>
                {
                    var qrCodeBytes = GenerateGiroCodeQR(invoice, company);
                    qrColumn.Item().Width(80).Height(80).Image(qrCodeBytes);
                    qrColumn.Item().PaddingTop(3).Text("QR-Code für Überweisung")
                        .FontSize(7)
                        .FontColor(Colors.TextSecondary)
                        .AlignCenter();
                });
            });

            if (!string.IsNullOrWhiteSpace(company.PdfFooterText))
            {
                column.Item().PaddingTop(20).Text(ReplacePdfPlaceholders(company.PdfFooterText, invoice, company)).FontSize(10).Bold();
            }
            else
            {
                column.Item().PaddingTop(20).Text("Vielen Dank für Ihr Vertrauen!").FontSize(10).Bold();
            }
        });
    }

    private void ComposeFooter(IContainer container, Company company)
    {
        container.AlignCenter().Column(column =>
        {
            column.Item().BorderTop(1).BorderColor(Colors.Divider).PaddingTop(10);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(leftColumn =>
                {
                    // Bei Kleinunternehmern muss der vollständige bürgerliche Name im Footer stehen
                    leftColumn.Item().Text(company.OwnerFullName).FontSize(8).FontColor(Colors.TextSecondary);
                    leftColumn.Item().Text($"Steuernr.: {company.TaxNumber}").FontSize(8).FontColor(Colors.TextSecondary);
                });

                row.RelativeItem().AlignCenter().Column(centerColumn =>
                {
                    centerColumn.Item().Text(text =>
                    {
                        text.CurrentPageNumber().FontSize(8).FontColor(Colors.TextSecondary);
                        text.Span(" / ").FontSize(8).FontColor(Colors.TextSecondary);
                        text.TotalPages().FontSize(8).FontColor(Colors.TextSecondary);
                    });
                });

                row.RelativeItem().AlignRight().Column(rightColumn =>
                {
                    rightColumn.Item().Text($"{company.BankName}").FontSize(8).FontColor(Colors.TextSecondary);
                    rightColumn.Item().Text($"IBAN: {company.BankAccount}").FontSize(8).FontColor(Colors.TextSecondary);
                });
            });
        });
    }

    private byte[] GenerateGiroCodeQR(Invoice invoice, Company company)
    {
        // GiroCode (EPC QR-Code) Format - European Payment Council Standard
        var accountHolder = !string.IsNullOrWhiteSpace(company.AccountHolder)
            ? company.AccountHolder
            : company.OwnerFullName;

        // Use AmountDue if there are down payments, otherwise use TotalGross
        var paymentAmount = invoice.TotalDownPayments > 0 ? invoice.AmountDue : invoice.TotalGross;
        var amount = paymentAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        var purpose = $"Rechnung {invoice.InvoiceNumber}";

        // GiroCode Format
        var giroCodeData = string.Join("\n", new[]
        {
            "BCD",                          // Service Tag
            "002",                          // Version
            "1",                            // Character Set (1 = UTF-8)
            "SCT",                          // Identification
            company.Bic ?? "",              // BIC (optional)
            accountHolder,                  // Beneficiary Name
            company.BankAccount,            // Beneficiary Account (IBAN)
            $"EUR{amount}",                 // Amount
            "",                             // Purpose (empty)
            purpose,                        // Remittance Information (Structured)
            "",                             // Remittance Information (Unstructured)
            ""                              // Beneficiary to originator information
        });

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(giroCodeData, QRCodeGenerator.ECCLevel.M);

        // Use SkiaSharp for cross-platform QR code rendering
        var qrCode = new PngByteQRCode(qrCodeData);
        var qrCodeBytes = qrCode.GetGraphic(20);

        return qrCodeBytes;
    }

    private string ReplacePdfPlaceholders(string text, Invoice invoice, Company company)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var firmenname = !string.IsNullOrEmpty(company.BusinessName)
            ? company.BusinessName
            : company.OwnerFullName;

        return text
            .Replace("{{Firmenname}}", firmenname)
            .Replace("{{Rechnungsnummer}}", invoice.InvoiceNumber)
            .Replace("{{Rechnungsdatum}}", invoice.InvoiceDate.ToString("dd.MM.yyyy", _germanCulture))
            .Replace("{{Faelligkeitsdatum}}", invoice.DueDate?.ToString("dd.MM.yyyy", _germanCulture) ?? "")
            .Replace("{{Rechnungsbetrag}}", invoice.TotalGross.ToString("C2", _germanCulture))
            .Replace("{{Kundenname}}", invoice.Customer?.Name ?? "");
    }
}
