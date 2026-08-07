using System.Globalization;
using System.Linq;
using Kuestencode.Core.Models;
using Kuestencode.Core.Interfaces;
using Kuestencode.Rapport.Models.Timesheets;
using Kuestencode.Rapport.Models;
using Kuestencode.Shared.Contracts.Rapport;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kuestencode.Rapport.Services;

/// <summary>
/// Generates a PDF timesheet document.
/// </summary>
public class TimesheetPdfService
{
    private readonly TimesheetExportService _exportService;
    private readonly ICompanyService _companyService;
    private readonly SettingsService _settingsService;
    private readonly ILogger<TimesheetPdfService> _logger;

    public TimesheetPdfService(
        TimesheetExportService exportService,
        ICompanyService companyService,
        SettingsService settingsService,
        ILogger<TimesheetPdfService> logger)
    {
        _exportService = exportService;
        _companyService = companyService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<(byte[] Bytes, string FileName)> GenerateAsync(TimesheetExportRequestDto request)
    {
        var timesheet = await _exportService.BuildAsync(request);
        var company = await _companyService.GetCompanyAsync();
        var settings = await _settingsService.GetSettingsAsync();

        var fileName = BuildFileName(request, timesheet);

        _logger.LogInformation("TimesheetPdfService: render start (CustomerId={CustomerId})", request.CustomerId);
        var document = new TimesheetDocument(timesheet, company, settings);
        var pdf = document.GeneratePdf();
        _logger.LogInformation("TimesheetPdfService: render done (CustomerId={CustomerId}, Size={Size})", request.CustomerId, pdf.Length);

        return (pdf, fileName);
    }


    public async Task<byte[]> RenderAsync(TimesheetDto timesheet)
    {
        var company = await _companyService.GetCompanyAsync();
        var settings = await _settingsService.GetSettingsAsync();
        var document = new TimesheetDocument(timesheet, company, settings);
        return document.GeneratePdf();
    }

    /// <summary>
    /// Synchronous render method for use in preview callbacks where async is not supported.
    /// Requires pre-loaded company and settings.
    /// </summary>
    public byte[] Render(TimesheetDto timesheet, Company company, RapportSettings settings)
    {
        var document = new TimesheetDocument(timesheet, company, settings);
        return document.GeneratePdf();
    }

    private static string BuildFileName(TimesheetExportRequestDto request, TimesheetDto timesheet)
    {
        if (!string.IsNullOrWhiteSpace(request.FileName))
        {
            var raw = request.FileName.Trim();
            if (!raw.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                raw += ".pdf";
            }

            return SanitizeFileName(raw);
        }

        var title = string.IsNullOrWhiteSpace(request.Title) ? timesheet.Title : request.Title.Trim();
        var customer = timesheet.Customer.Name;
        var period = timesheet.From.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var baseName = $"{title}_{customer}_{period}.pdf";

        return SanitizeFileName(baseName);
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName;
    }

    private sealed class TimesheetDocument : IDocument
    {
        private readonly TimesheetDto _timesheet;
        private readonly Company _company;
        private readonly RapportSettings _settings;
        private readonly bool _includeDescription;

        public TimesheetDocument(TimesheetDto timesheet, Company company, RapportSettings settings)
        {
            _timesheet = timesheet;
            _company = company;
            _settings = settings;
            _includeDescription = timesheet.Groups
                .SelectMany(g => g.Entries)
                .Any(e => !string.IsNullOrWhiteSpace(e.Description));
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().Column(col =>
                {
                    var nowInBerlin = RapportTimeZone.UtcToBerlin(DateTime.UtcNow);
                    col.Item().Text($"Erstellt am {nowInBerlin:dd.MM.yyyy}").FontSize(8).FontColor(Colors.Grey.Medium);
                    if (!string.IsNullOrWhiteSpace(_settings.PdfFooterText))
                    {
                        col.Item().Text(_settings.PdfFooterText).FontSize(8).FontColor(Colors.Grey.Medium);
                    }
                });
            });
        }

        private void ComposeHeader(IContainer container)
        {
            var primary = _settings.PdfPrimaryColor;
            var accent = _settings.PdfAccentColor;

            container.Column(col =>
            {
                col.Item().Element(header =>
                {
                    if (_settings.PdfLayout == Kuestencode.Core.Enums.PdfLayout.Betont)
                    {
                        header.Background(primary).Padding(12).Row(row =>
                        {
                            row.RelativeItem(1).AlignLeft().AlignMiddle().Height(50).Element(logo =>
                            {
                                if (_company.LogoData != null && _company.LogoData.Length > 0)
                                {
                                    logo.Image(_company.LogoData).FitHeight();
                                }
                                else
                                {
                                    logo.Text(_company.DisplayName).FontSize(14).Bold().FontColor(Colors.White);
                                }
                            });

                            row.RelativeItem(2).AlignRight().AlignMiddle().Text(text =>
                            {
                                text.Line(_timesheet.Title).FontSize(18).Bold().FontColor(Colors.White);
                                text.Line($"Zeitraum: {_timesheet.From:dd.MM.yyyy} - {_timesheet.To:dd.MM.yyyy}")
                                    .FontSize(10).FontColor(Colors.White);
                            });
                        });
                    }
                    else
                    {
                        header.Row(row =>
                        {
                            row.RelativeItem(1).AlignLeft().AlignMiddle().Height(50).Element(logo =>
                            {
                                if (_company.LogoData != null && _company.LogoData.Length > 0)
                                {
                                    logo.Image(_company.LogoData).FitHeight();
                                }
                                else
                                {
                                    logo.Text(_company.DisplayName).FontSize(14).Bold().FontColor(primary);
                                }
                            });

                            row.RelativeItem(2).AlignRight().AlignMiddle().Text(text =>
                            {
                                text.Line(_timesheet.Title).FontSize(18).Bold().FontColor(primary);
                                text.Line($"Zeitraum: {_timesheet.From:dd.MM.yyyy} - {_timesheet.To:dd.MM.yyyy}")
                                    .FontSize(10).FontColor(Colors.Grey.Darken2);
                            });
                        });
                    }
                });

                if (!string.IsNullOrWhiteSpace(_settings.PdfHeaderText))
                {
                    col.Item().PaddingTop(6).Text(_settings.PdfHeaderText).FontSize(9).FontColor(accent);
                }
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.Column(column =>
            {
                column.Spacing(16);

                column.Item().Element(ComposeCustomerBlock);

                foreach (var group in _timesheet.Groups)
                {
                    column.Item().Element(c => ComposeGroup(c, group));
                }

                column.Item().Element(ComposeTotals);
            });
        }

        private void ComposeCustomerBlock(IContainer container)
        {
            var borderColor = _settings.PdfLayout == Kuestencode.Core.Enums.PdfLayout.Strukturiert ? _settings.PdfAccentColor : "#E5E7EB";
            var background = _settings.PdfLayout == Kuestencode.Core.Enums.PdfLayout.Strukturiert ? "#FFFFFF" : "#F5F5F5";
            container.Background(background).Border(1).BorderColor(borderColor).Padding(12).Column(col =>
            {
                col.Item().Text($"Für: {_timesheet.Customer.Name}").Bold().FontSize(12);
                if (!string.IsNullOrWhiteSpace(_timesheet.Customer.CustomerNumber))
                {
                    col.Item().Text($"Kundennummer: {_timesheet.Customer.CustomerNumber}");
                }
                if (!string.IsNullOrWhiteSpace(_timesheet.Customer.Address))
                {
                    col.Item().Text(_timesheet.Customer.Address);
                }
                var cityLine = string.Join(" ", new[] { _timesheet.Customer.PostalCode, _timesheet.Customer.City }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(cityLine))
                {
                    col.Item().Text(cityLine);
                }
                if (!string.IsNullOrWhiteSpace(_timesheet.Customer.Country))
                {
                    col.Item().Text(_timesheet.Customer.Country);
                }
            });
        }

        private void ComposeGroup(IContainer container, TimesheetProjectGroupDto group)
        {
            container.Column(col =>
            {
                col.Spacing(6);
                var showHeader = !(_timesheet.Project is null && group.ProjectId is null);
                if (showHeader)
                {
                    col.Item().Text(group.ProjectName).FontSize(12).Bold();
                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                }
                col.Item().Element(c => ComposeEntriesTable(c, group));
                col.Item().AlignRight().Text($"Zwischensumme: {FormatHours(group.SubtotalHours)} h").Bold();
            });
        }

        private void ComposeEntriesTable(IContainer container, TimesheetProjectGroupDto group)
        {
            var includeAmount = _timesheet.HourlyRate.HasValue;
            var includeEmployee = _timesheet.ShowEmployeeColumn;

            container.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(70);
                    if (_includeDescription)
                        cols.RelativeColumn();
                    if (includeEmployee)
                        cols.ConstantColumn(80);
                    cols.ConstantColumn(55);
                    cols.ConstantColumn(55);
                    cols.ConstantColumn(55);
                    if (includeAmount)
                        cols.ConstantColumn(70);
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellHeader).Text("Datum");
                    if (_includeDescription)
                        header.Cell().Element(CellHeader).Text("Beschreibung");
                    if (includeEmployee)
                        header.Cell().Element(CellHeader).Text("Mitarbeiter");
                    header.Cell().Element(CellHeader).AlignRight().Text("Start");
                    header.Cell().Element(CellHeader).AlignRight().Text("Ende");
                    header.Cell().Element(CellHeader).AlignRight().Text("Dauer");
                    if (includeAmount)
                        header.Cell().Element(CellHeader).AlignRight().Text("Betrag");
                });

                foreach (var entry in group.Entries)
                {
                    var durationHours = (decimal)entry.Duration.TotalHours;
                    var amount = includeAmount ? durationHours * _timesheet.HourlyRate!.Value : 0m;

                    table.Cell().Element(CellBody).Text(entry.Date.ToString("dd.MM.yyyy"));
                    if (_includeDescription)
                        table.Cell().Element(CellBody).Text(string.IsNullOrWhiteSpace(entry.Description) ? "–" : entry.Description);
                    if (includeEmployee)
                        table.Cell().Element(CellBody).Text(string.IsNullOrWhiteSpace(entry.EmployeeName) ? "–" : entry.EmployeeName);
                    var startInBerlin = RapportTimeZone.UtcToBerlin(entry.StartTime);
                    var endInBerlin = entry.EndTime.HasValue ? RapportTimeZone.UtcToBerlin(entry.EndTime.Value) : (DateTime?)null;
                    table.Cell().Element(CellBody).AlignRight().Text(startInBerlin.ToString("HH:mm"));
                    table.Cell().Element(CellBody).AlignRight().Text(endInBerlin?.ToString("HH:mm") ?? "–");
                    table.Cell().Element(CellBody).AlignRight().Text(FormatDuration(entry.Duration));
                    if (includeAmount)
                        table.Cell().Element(CellBody).AlignRight().Text(amount.ToString("C", CultureInfo.GetCultureInfo("de-DE")));
                }
            });
        }

        private void ComposeTotals(IContainer container)
        {
            container.PaddingTop(10).Column(col =>
            {
                col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                col.Item().AlignRight().Text($"GESAMT: {FormatHours(_timesheet.TotalHours)} h").FontSize(12).Bold();

                if (_timesheet.HourlyRate.HasValue)
                {
                    col.Item().AlignRight().Text($"Stundensatz: {_timesheet.HourlyRate.Value.ToString("C", CultureInfo.GetCultureInfo("de-DE"))}/h");
                    if (_timesheet.TotalAmount.HasValue)
                    {
                        col.Item().AlignRight().Text($"Gesamtbetrag: {_timesheet.TotalAmount.Value.ToString("C", CultureInfo.GetCultureInfo("de-DE"))}").Bold();
                    }
                }
            });
        }

        private static string FormatDuration(TimeSpan duration)
        {
            var totalMinutes = (int)Math.Round(duration.TotalMinutes);
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return $"{hours}:{minutes:D2}";
        }

        private static string FormatHours(decimal hours)
        {
            var totalMinutes = (int)Math.Round(hours * 60);
            var h = totalMinutes / 60;
            var m = totalMinutes % 60;
            return $"{h}:{m:D2}";
        }

        private IContainer CellHeader(IContainer container)
        {
            var style = container.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
            if (_settings.PdfLayout != Kuestencode.Core.Enums.PdfLayout.Klar)
            {
                style = style.Background(_settings.PdfPrimaryColor).PaddingHorizontal(4).DefaultTextStyle(x => x.SemiBold().FontColor(Colors.White));
            }
            return style;
        }

        private static IContainer CellBody(IContainer container)
        {
            return container.PaddingVertical(3).BorderBottom(1).BorderColor(Colors.Grey.Lighten4);
        }
    }
}


