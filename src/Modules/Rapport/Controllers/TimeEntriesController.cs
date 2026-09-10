using System.ComponentModel.DataAnnotations;
using Kuestencode.Rapport.Models;
using Kuestencode.Rapport.Services;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.UI.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Kuestencode.Rapport.Controllers;

[ApiController]
[Route("api/rapport/entries")]
[RequireRole(UserRole.Admin, UserRole.Buero, UserRole.Mitarbeiter)]
public class TimeEntriesController : ControllerBase
{
    private readonly TimeEntryService _timeEntryService;
    private readonly IUserContextService _userContextService;
    private readonly TeamMemberCacheService _teamMemberCacheService;

    public TimeEntriesController(
        TimeEntryService timeEntryService,
        IUserContextService userContextService,
        TeamMemberCacheService teamMemberCacheService)
    {
        _timeEntryService = timeEntryService;
        _userContextService = userContextService;
        _teamMemberCacheService = teamMemberCacheService;
    }

    /// <summary>
    /// GET /api/rapport/entries
    /// - Wenn Mitarbeiter: Automatisch nach eigenem TeamMemberId filtern
    /// - Wenn Admin/Büro: Alle, optional ?teamMemberId= Filter
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<TimeEntry>>> GetEntries(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int[]? customerIds = null,
        [FromQuery] int[]? projectIds = null,
        [FromQuery] bool? manualOnly = null,
        [FromQuery] bool? onlyWithoutProject = null,
        [FromQuery] Guid? teamMemberId = null)
    {
        var isAdminOrBuero = await _userContextService.IsAdminOrBueroAsync();
        var currentUserId = await _userContextService.GetCurrentUserIdAsync();

        // Mitarbeiter sehen nur eigene Einträge
        if (!isAdminOrBuero && currentUserId.HasValue)
        {
            teamMemberId = currentUserId.Value;
        }

        var entries = await _timeEntryService.GetEntriesAsync(
            from,
            to,
            customerIds,
            projectIds,
            manualOnly,
            onlyWithoutProject,
            teamMemberId);

        return Ok(entries);
    }

    /// <summary>
    /// GET /api/rapport/entries/{id}
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<TimeEntry>> GetEntry(int id)
    {
        var entry = await _timeEntryService.GetEntryAsync(id);
        if (entry == null)
        {
            return NotFound();
        }

        // Prüfen, ob User den Eintrag sehen darf
        var isAdminOrBuero = await _userContextService.IsAdminOrBueroAsync();
        var currentUserId = await _userContextService.GetCurrentUserIdAsync();

        if (!isAdminOrBuero && entry.TeamMemberId != currentUserId)
        {
            return Forbid();
        }

        return Ok(entry);
    }

    /// <summary>
    /// POST /api/rapport/entries
    /// - Wenn Mitarbeiter: TeamMemberId automatisch auf eigene setzen, ignoriere Body-Wert
    /// - Wenn Admin/Büro: TeamMemberId aus Body nehmen
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TimeEntry>> CreateEntry([FromBody] CreateTimeEntryDto dto)
    {
        var isAdminOrBuero = await _userContextService.IsAdminOrBueroAsync();
        var currentUserId = await _userContextService.GetCurrentUserIdAsync();
        var currentUserName = await _userContextService.GetCurrentUserNameAsync();

        Guid? teamMemberId;
        string? teamMemberName;

        if (isAdminOrBuero)
        {
            // Admin/Büro können für andere erfassen
            teamMemberId = dto.TeamMemberId ?? currentUserId;
            if (teamMemberId.HasValue && teamMemberId != currentUserId)
            {
                var member = await _teamMemberCacheService.GetByIdAsync(teamMemberId.Value);
                teamMemberName = member?.DisplayName ?? currentUserName;
            }
            else
            {
                teamMemberName = currentUserName;
            }
        }
        else
        {
            // Mitarbeiter: Immer eigene TeamMemberId
            teamMemberId = currentUserId;
            teamMemberName = currentUserName;
        }

        try
        {
            var member = teamMemberId.HasValue
                ? await _teamMemberCacheService.GetByIdAsync(teamMemberId.Value)
                : null;

            var entry = await _timeEntryService.CreateManualEntryAsync(
                dto.StartTime,
                dto.EndTime,
                dto.ProjectId,
                dto.CustomerId,
                dto.Description,
                teamMemberId,
                teamMemberName,
                member?.MitarbeiterRolleId,
                member?.MitarbeiterRolleName,
                dto.BreakMinutes);

            return CreatedAtAction(nameof(GetEntry), new { id = entry.Id }, entry);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// PUT /api/rapport/entries/{id}
    /// - Wenn Mitarbeiter: Nur eigene, nur wenn &lt; 14 Tage alt
    /// - Wenn Admin/Büro: Alle
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<TimeEntry>> UpdateEntry(int id, [FromBody] UpdateTimeEntryDto dto)
    {
        var entry = await _timeEntryService.GetEntryAsync(id);
        if (entry == null)
        {
            return NotFound();
        }

        // Berechtigung prüfen
        if (!await _timeEntryService.CanEditEntryAsync(entry))
        {
            return Forbid();
        }

        try
        {
            var updated = await _timeEntryService.UpdateEntryAsync(
                id,
                dto.StartTime,
                dto.EndTime,
                dto.ProjectId,
                dto.CustomerId,
                dto.Description,
                entry.TeamMemberId,
                entry.TeamMemberName,
                dto.BreakMinutes);

            return Ok(updated);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// DELETE /api/rapport/entries/{id}
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteEntry(int id)
    {
        var entry = await _timeEntryService.GetEntryAsync(id);
        if (entry == null)
        {
            return NotFound();
        }

        // Berechtigung prüfen
        if (!await _timeEntryService.CanEditEntryAsync(entry))
        {
            return Forbid();
        }

        try
        {
            await _timeEntryService.SoftDeleteEntryAsync(id);
            return NoContent();
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("project/{projectId:int}/mark-invoiced")]
    [RequireRole(UserRole.Admin, UserRole.Buero)]
    public async Task<IActionResult> MarkProjectEntriesAsInvoiced(int projectId)
    {
        await _timeEntryService.MarkProjectEntriesAsInvoicedAsync(projectId);
        return NoContent();
    }
}

public class CreateTimeEntryDto
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int? ProjectId { get; set; }
    public int? CustomerId { get; set; }
    public string? Description { get; set; }
    public Guid? TeamMemberId { get; set; }
    public int BreakMinutes { get; set; }
}

public class UpdateTimeEntryDto
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int? ProjectId { get; set; }
    public int? CustomerId { get; set; }
    public string? Description { get; set; }
    public int BreakMinutes { get; set; }
}
