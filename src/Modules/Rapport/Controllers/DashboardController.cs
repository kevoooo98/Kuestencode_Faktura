using Kuestencode.Rapport.Services;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.Contracts.Rapport;
using Kuestencode.Shared.UI.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Kuestencode.Rapport.Controllers;

[ApiController]
[Route("api/dashboard")]
[RequireRole(UserRole.Admin, UserRole.Buero, UserRole.Mitarbeiter)]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboardService;
    private readonly SettingsService _settingsService;
    private readonly TimeRoundingService _roundingService;

    public DashboardController(
        DashboardService dashboardService,
        SettingsService settingsService,
        TimeRoundingService roundingService)
    {
        _dashboardService = dashboardService;
        _settingsService = settingsService;
        _roundingService = roundingService;
    }

    [HttpGet("projects/{projectId:int}/hours")]
    public async Task<ActionResult<ProjectHoursResponseDto>> GetProjectHours(int projectId)
    {
        var entries = await _dashboardService.GetEntriesAsync(
            DateTime.MinValue,
            DateTime.UtcNow,
            customerIds: null,
            projectIds: new[] { projectId });

        var settings = await _settingsService.GetSettingsAsync();
        var roundingMinutes = settings.RoundingMinutes;

        var totalHours = 0m;
        var now = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            var gross = (entry.EndTime ?? now) - entry.StartTime;
            var net = gross - TimeSpan.FromMinutes(entry.BreakMinutes);
            if (net < TimeSpan.Zero) net = TimeSpan.Zero;
            var duration = roundingMinutes > 0 ? _roundingService.RoundDuration(net, roundingMinutes) : net;
            totalHours += (decimal)duration.TotalHours;
        }

        return Ok(new ProjectHoursResponseDto
        {
            ProjectId = projectId,
            TotalHours = Math.Round(totalHours, 2)
        });
    }

    [HttpGet("projects/{projectId:int}/hours/by-type")]
    public async Task<ActionResult<ProjectHoursByTypeResponseDto>> GetProjectHoursByType(int projectId)
    {
        var entries = await _dashboardService.GetEntriesAsync(
            DateTime.MinValue,
            DateTime.UtcNow,
            customerIds: null,
            projectIds: new[] { projectId });

        var settings = await _settingsService.GetSettingsAsync();
        var roundingMinutes = settings.RoundingMinutes;
        var now = DateTime.UtcNow;

        var offenByRolle = new Dictionary<int, (string Name, decimal Stunden)>();
        var invoicedByRolle = new Dictionary<int, (string Name, decimal Stunden)>();

        foreach (var entry in entries)
        {
            var gross = (entry.EndTime ?? now) - entry.StartTime;
            var net = gross - TimeSpan.FromMinutes(entry.BreakMinutes);
            if (net < TimeSpan.Zero) net = TimeSpan.Zero;
            var duration = roundingMinutes > 0 ? _roundingService.RoundDuration(net, roundingMinutes) : net;

            var stunden = (decimal)duration.TotalHours;
            var rolleId = entry.MitarbeiterRolleId ?? 0;
            var rolleName = entry.MitarbeiterRolleName ?? "Unbekannt";
            var target = entry.IsInvoiced ? invoicedByRolle : offenByRolle;

            if (target.TryGetValue(rolleId, out var existing))
                target[rolleId] = (existing.Name, existing.Stunden + stunden);
            else
                target[rolleId] = (rolleName, stunden);
        }

        static List<ProjectHoursByRolleDto> ToList(Dictionary<int, (string Name, decimal Stunden)> dict) =>
            dict.Select(kvp => new ProjectHoursByRolleDto
            {
                RolleId = kvp.Key,
                RolleName = kvp.Value.Name,
                Stunden = Math.Round(kvp.Value.Stunden, 2)
            }).OrderBy(r => r.RolleId).ToList();

        return Ok(new ProjectHoursByTypeResponseDto
        {
            ProjectId = projectId,
            StundenByRolle = ToList(offenByRolle),
            InvoicedStundenByRolle = ToList(invoicedByRolle)
        });
    }

    [HttpGet("projects/{projectId:int}/hours/by-member")]
    public async Task<ActionResult<ProjectHoursByMemberResponseDto>> GetProjectHoursByMember(int projectId)
    {
        var entries = await _dashboardService.GetEntriesAsync(
            DateTime.MinValue,
            DateTime.UtcNow,
            customerIds: null,
            projectIds: new[] { projectId });

        var settings = await _settingsService.GetSettingsAsync();
        var roundingMinutes = settings.RoundingMinutes;
        var now = DateTime.UtcNow;

        var byMember = new Dictionary<Guid, (string Name, decimal Stunden)>();

        foreach (var entry in entries)
        {
            var gross = (entry.EndTime ?? now) - entry.StartTime;
            var net = gross - TimeSpan.FromMinutes(entry.BreakMinutes);
            if (net < TimeSpan.Zero) net = TimeSpan.Zero;
            var duration = roundingMinutes > 0 ? _roundingService.RoundDuration(net, roundingMinutes) : net;

            var stunden = (decimal)duration.TotalHours;
            var memberId = entry.TeamMemberId ?? Guid.Empty;
            var memberName = entry.TeamMemberName ?? "Unbekannt";

            if (byMember.TryGetValue(memberId, out var existing))
                byMember[memberId] = (existing.Name, existing.Stunden + stunden);
            else
                byMember[memberId] = (memberName, stunden);
        }

        return Ok(new ProjectHoursByMemberResponseDto
        {
            ProjectId = projectId,
            StundenByMitarbeiter = byMember
                .Select(kvp => new ProjectHoursByMemberDto
                {
                    TeamMemberId = kvp.Key,
                    TeamMemberName = kvp.Value.Name,
                    Stunden = Math.Round(kvp.Value.Stunden, 2)
                })
                .OrderBy(m => m.TeamMemberName)
                .ToList()
        });
    }
}
