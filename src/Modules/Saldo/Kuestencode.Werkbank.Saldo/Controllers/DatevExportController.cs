using Kuestencode.Core.Auth;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.UI.Auth;
using Kuestencode.Werkbank.Saldo.Domain.Dtos;
using Kuestencode.Werkbank.Saldo.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kuestencode.Werkbank.Saldo.Controllers;

[ApiController]
[Route("api/saldo/export")]
[RequireRole(UserRole.Admin, UserRole.Buero)]
public class DatevExportController : ControllerBase
{
    private readonly IDatevExportService _exportService;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILogger<DatevExportController> _logger;

    public DatevExportController(
        IDatevExportService exportService,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<DatevExportController> logger)
    {
        _exportService = exportService;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Exportiert einen DATEV-Buchungsstapel im EXTF-Format (Windows-1252 CSV).
    /// Zufluss-/Abflussprinzip: maßgeblich ist das Zahlungsdatum.
    /// </summary>
    [HttpGet("datev")]
    public async Task<IActionResult> ExportDatevBuchungsstapel(
        [FromQuery] DateOnly? von = null,
        [FromQuery] DateOnly? bis = null)
    {
        var (vonDate, bisDate) = GetDateRange(von, bis);
        try
        {
            var bytes = await _exportService.ExportBuchungsstapelAsync(vonDate, bisDate, _currentUserAccessor.Get().UserId);
            var fileName = $"EXTF_Buchungsstapel_{vonDate:yyyy}_{GetQuartal(vonDate, bisDate)}.csv";
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            return File(bytes, "text/csv; charset=windows-1252", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim DATEV-Buchungsstapel-Export für {Von} - {Bis}", vonDate, bisDate);
            return StatusCode(500, "Fehler beim Erstellen des DATEV-Exports.");
        }
    }

    /// <summary>
    /// Exportiert alle Belege des Zeitraums als ZIP-Archiv.
    /// Enthält Faktura-Rechnungen als PDF und Recepta-Belegdateien.
    /// </summary>
    [HttpGet("belege")]
    public async Task<IActionResult> ExportBelege(
        [FromQuery] DateOnly? von = null,
        [FromQuery] DateOnly? bis = null)
    {
        var (vonDate, bisDate) = GetDateRange(von, bis);
        try
        {
            var bytes = await _exportService.ExportBelegeAsync(vonDate, bisDate, _currentUserAccessor.Get().UserId);
            var fileName = $"Belege_{vonDate:yyyy}_{GetQuartal(vonDate, bisDate)}.zip";
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            return File(bytes, "application/zip", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Belege-Export für {Von} - {Bis}", vonDate, bisDate);
            return StatusCode(500, "Fehler beim Erstellen des Belege-Exports.");
        }
    }

    /// <summary>
    /// Gibt die Export-Historie zurück, neueste zuerst.
    /// </summary>
    [HttpGet("historie")]
    public async Task<ActionResult<List<ExportLogDto>>> GetExportHistorie()
    {
        var historie = await _exportService.GetExportHistorieAsync();
        return Ok(historie);
    }

    private static (DateOnly Von, DateOnly Bis) GetDateRange(DateOnly? von, DateOnly? bis)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return (
            von ?? new DateOnly(today.Year, 1, 1),
            bis ?? new DateOnly(today.Year, 12, 31)
        );
    }

    /// <summary>
    /// Ermittelt eine Quartal-/Perioden-Bezeichnung für den Dateinamen.
    /// Ganzes Jahr → "FJ", einzelnes Quartal → "Q1"–"Q4", sonst → "Periode".
    /// </summary>
    private static string GetQuartal(DateOnly von, DateOnly bis)
    {
        if (von.Month == 1 && von.Day == 1 && bis.Month == 12 && bis.Day == 31)
            return "FJ";

        var monat = von.Month;
        return monat switch
        {
            1 => "Q1",
            4 => "Q2",
            7 => "Q3",
            10 => "Q4",
            _ => $"{von:MMdd}_{bis:MMdd}"
        };
    }
}
