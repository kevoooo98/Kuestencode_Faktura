using System.Globalization;
using System.Linq;
using System.Text;
using Kuestencode.Rapport.Models.Timesheets;
using Kuestencode.Shared.Contracts.Rapport;

namespace Kuestencode.Rapport.Services;

/// <summary>
/// Generates CSV exports for timesheets.
/// </summary>
public class TimesheetCsvService
{
    private readonly TimesheetExportService _exportService;

    public TimesheetCsvService(TimesheetExportService exportService)
    {
        _exportService = exportService;
    }

    public async Task<(byte[] Bytes, string FileName)> GenerateAsync(TimesheetExportRequestDto request)
    {
        var timesheet = await _exportService.BuildAsync(request);
        var includeAmount = timesheet.HourlyRate.HasValue;
        var includeEmployee = timesheet.ShowEmployeeColumn;
        var includeDescription = timesheet.Groups
            .SelectMany(g => g.Entries)
            .Any(e => !string.IsNullOrWhiteSpace(e.Description));

        var sb = new StringBuilder();
        var headers = new List<string> { "Datum", "Projekt" };
        if (includeDescription)
        {
            headers.Add("Beschreibung");
        }
        if (includeEmployee)
        {
            headers.Add("Mitarbeiter");
        }
        headers.AddRange(new[] { "Start", "Ende", "Dauer" });
        if (includeAmount)
        {
            headers.Add("Betrag");
        }

        sb.AppendLine(string.Join(";", headers));

        foreach (var group in timesheet.Groups)
        {
            var projectName = string.IsNullOrWhiteSpace(group.ProjectName) ? "Ohne Projekt" : group.ProjectName;
            foreach (var entry in group.Entries)
            {
                var durationHours = (decimal)entry.Duration.TotalHours;
                var durationText = FormatDuration(entry.Duration);
                var startInBerlin = RapportTimeZone.UtcToBerlin(entry.StartTime);
                var endInBerlin = entry.EndTime.HasValue ? RapportTimeZone.UtcToBerlin(entry.EndTime.Value) : (DateTime?)null;
                var startText = startInBerlin.ToString("HH:mm");
                var endText = endInBerlin?.ToString("HH:mm") ?? "";

                var row = new List<string>
                {
                    entry.Date.ToString("dd.MM.yyyy"),
                    Escape(projectName)
                };

                if (includeDescription)
                {
                    row.Add(Escape(entry.Description));
                }

                if (includeEmployee)
                {
                    row.Add(Escape(entry.EmployeeName));
                }

                row.Add(startText);
                row.Add(endText);
                row.Add(durationText);

                if (includeAmount)
                {
                    var amount = durationHours * timesheet.HourlyRate!.Value;
                    row.Add(amount.ToString("F2", CultureInfo.GetCultureInfo("de-DE")));
                }

                sb.AppendLine(string.Join(";", row));
            }
        }

        var fileName = BuildFileName(request, timesheet);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
        return (bytes, fileName);
    }

    private static string BuildFileName(TimesheetExportRequestDto request, TimesheetDto timesheet)
    {
        if (!string.IsNullOrWhiteSpace(request.FileName))
        {
            var raw = request.FileName.Trim();
            if (!raw.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                raw += ".csv";
            }

            return SanitizeFileName(raw);
        }

        var title = string.IsNullOrWhiteSpace(request.Title) ? timesheet.Title : request.Title.Trim();
        var customer = timesheet.Customer.Name;
        var period = timesheet.From.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var baseName = $"{title}_{customer}_{period}.csv";

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

    private static string FormatDuration(TimeSpan duration)
    {
        var totalMinutes = (int)Math.Round(duration.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"{hours}:{minutes:D2}";
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (cleaned.Contains(';') || cleaned.Contains('"'))
        {
            cleaned = '"' + cleaned.Replace("\"", "\"\"") + '"';
        }

        return cleaned;
    }
}

