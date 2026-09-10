using Microsoft.AspNetCore.Mvc;
using Kuestencode.Werkbank.Offerte.Services;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.UI.Auth;

namespace Kuestencode.Werkbank.Offerte.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequireRole(UserRole.Admin, UserRole.Buero)]
public class AngebotController : ControllerBase
{
    private readonly IOfferteDruckService _druckService;
    private readonly ILogger<AngebotController> _logger;

    public AngebotController(IOfferteDruckService druckService, ILogger<AngebotController> logger)
    {
        _druckService = druckService;
        _logger = logger;
    }

    [HttpGet("{id:guid}/pdf-print")]
    public async Task<IActionResult> GetPdfForPrint(Guid id)
    {
        try
        {
            var pdfBytes = await _druckService.DruckvorbereitungAsync(id);
            var base64 = Convert.ToBase64String(pdfBytes);

            var html = $$"""
                <!DOCTYPE html>
                <html>
                <head>
                  <title>Angebot</title>
                  <style>html,body,embed{margin:0;padding:0;width:100%;height:100%;}</style>
                </head>
                <body>
                  <embed src="data:application/pdf;base64,{{base64}}" type="application/pdf" width="100%" height="100%">
                  <script>
                    setTimeout(function() { window.print(); }, 1500);
                  </script>
                </body>
                </html>
                """;

            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating print PDF for Angebot {AngebotId}", id);
            return StatusCode(500, "Internal server error");
        }
    }
}
