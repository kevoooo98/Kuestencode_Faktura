using Kuestencode.Rapport.Services;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.Contracts.Rapport;
using Kuestencode.Shared.UI.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Kuestencode.Rapport.Controllers;

[ApiController]
[Route("api/timesheets")]
[RequireRole(UserRole.Admin, UserRole.Buero)]
public class TimesheetController : ControllerBase
{
    private readonly TimesheetPdfService _pdfService;
    private readonly TimesheetCsvService _csvService;

    public TimesheetController(TimesheetPdfService pdfService, TimesheetCsvService csvService)
    {
        _pdfService = pdfService;
        _csvService = csvService;
    }

    [HttpPost("pdf")]
    public async Task<IActionResult> GeneratePdf([FromBody] TimesheetExportRequestDto request)
    {
        var result = await _pdfService.GenerateAsync(request);
        return File(result.Bytes, "application/pdf", result.FileName);
    }

    [HttpPost("csv")]
    public async Task<IActionResult> GenerateCsv([FromBody] TimesheetExportRequestDto request)
    {
        var result = await _csvService.GenerateAsync(request);
        return File(result.Bytes, "text/csv", result.FileName);
    }
}
