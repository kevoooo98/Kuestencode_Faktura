using Kuestencode.Core.Auth;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.UI.Auth;
using Kuestencode.Werkbank.Saldo.Domain.Dtos;
using Kuestencode.Werkbank.Saldo.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kuestencode.Werkbank.Saldo.Controllers;

[ApiController]
[Route("api/saldo/period-close")]
[RequireRole(UserRole.Admin, UserRole.Buero)]
public class PeriodCloseController : ControllerBase
{
    private readonly IPeriodCloseService _periodCloseService;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public PeriodCloseController(IPeriodCloseService periodCloseService, ICurrentUserAccessor currentUserAccessor)
    {
        _periodCloseService = periodCloseService;
        _currentUserAccessor = currentUserAccessor;
    }

    /// <summary>
    /// Schließt einen Zeitraum informativ ab (explizite Nutzeraktion nach einem Export).
    /// Blockiert keine Bearbeitung in Faktura/Recepta, dient nur als Hinweis.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PeriodCloseDto>> ClosePeriod([FromBody] ClosePeriodRequest request)
    {
        var result = await _periodCloseService.ClosePeriodAsync(
            request.Von, request.Bis, _currentUserAccessor.Get().UserId, request.ExportLogId);
        return Ok(result);
    }

    /// <summary>
    /// Gibt den zuletzt erfassten Abschluss für exakt diesen Zeitraum zurück, falls vorhanden.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PeriodCloseDto?>> GetForRange([FromQuery] DateOnly von, [FromQuery] DateOnly bis)
    {
        var result = await _periodCloseService.GetForRangeAsync(von, bis);
        return Ok(result);
    }

    /// <summary>
    /// Gibt alle erfassten Zeitraum-Abschlüsse zurück, neueste zuerst.
    /// </summary>
    [HttpGet("historie")]
    public async Task<ActionResult<List<PeriodCloseDto>>> GetHistorie()
    {
        var result = await _periodCloseService.GetAllAsync();
        return Ok(result);
    }
}

public record ClosePeriodRequest(DateOnly Von, DateOnly Bis, Guid? ExportLogId = null);
