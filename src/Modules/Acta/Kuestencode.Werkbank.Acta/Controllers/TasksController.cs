using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.UI.Auth;
using Kuestencode.Werkbank.Acta.Controllers.Dtos;
using Kuestencode.Werkbank.Acta.Domain.Dtos;
using Kuestencode.Werkbank.Acta.Domain.Entities;
using Kuestencode.Werkbank.Acta.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kuestencode.Werkbank.Acta.Controllers;

[ApiController]
[Route("api/acta")]
public class TasksController : ControllerBase
{
    private readonly IProjectTaskService _taskService;
    private readonly ILogger<TasksController> _logger;

    public TasksController(IProjectTaskService taskService, ILogger<TasksController> logger)
    {
        _taskService = taskService;
        _logger = logger;
    }

    /// <summary>
    /// Lädt alle Aufgaben eines Projekts.
    /// </summary>
    [HttpGet("projects/{projectId:guid}/tasks")]
    [RequireRole(UserRole.Admin, UserRole.Buero, UserRole.Mitarbeiter)]
    public async Task<ActionResult<List<ProjectTaskDto>>> GetByProject(Guid projectId)
    {
        var tasks = await _taskService.GetByProjectIdAsync(projectId);
        return Ok(tasks.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Lädt alle Aufgaben, die einem Benutzer zugewiesen sind.
    /// </summary>
    [HttpGet("tasks/assigned/{userId:guid}")]
    [RequireRole(UserRole.Admin, UserRole.Buero, UserRole.Mitarbeiter)]
    public async Task<ActionResult<List<AssignedProjectTaskDto>>> GetAssignedToUser(Guid userId)
    {
        var tasks = await _taskService.GetByAssignedUserIdAsync(userId);
        return Ok(tasks.Select(MapToAssignedDto).ToList());
    }

    /// <summary>
    /// Erstellt eine neue Aufgabe für ein Projekt.
    /// </summary>
    [HttpPost("projects/{projectId:guid}/tasks")]
    [RequireRole(UserRole.Admin, UserRole.Buero)]
    public async Task<ActionResult<ProjectTaskDto>> Create(Guid projectId, [FromBody] CreateTaskRequest request)
    {
        try
        {
            var dto = new CreateProjectTaskDto
            {
                Title = request.Title,
                Notes = request.Notes,
                TargetDate = request.TargetDate,
                AssignedUserId = request.AssignedUserId
            };

            var task = await _taskService.CreateAsync(projectId, dto);
            return CreatedAtAction(nameof(GetById), new { id = task.Id }, MapToDto(task));
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lädt eine Aufgabe anhand der ID.
    /// </summary>
    [HttpGet("tasks/{id:guid}")]
    [RequireRole(UserRole.Admin, UserRole.Buero, UserRole.Mitarbeiter)]
    public async Task<ActionResult<ProjectTaskDto>> GetById(Guid id)
    {
        var task = await _taskService.GetByIdAsync(id);
        if (task == null)
        {
            return NotFound();
        }

        return Ok(MapToDto(task));
    }

    /// <summary>
    /// Aktualisiert eine Aufgabe.
    /// </summary>
    [HttpPut("tasks/{id:guid}")]
    [RequireRole(UserRole.Admin, UserRole.Buero)]
    public async Task<ActionResult<ProjectTaskDto>> Update(Guid id, [FromBody] UpdateTaskRequest request)
    {
        try
        {
            var dto = new UpdateProjectTaskDto
            {
                Title = request.Title,
                Notes = request.Notes,
                TargetDate = request.TargetDate,
                AssignedUserId = request.AssignedUserId
            };

            var task = await _taskService.UpdateAsync(id, dto);
            return Ok(MapToDto(task));
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Markiert eine Aufgabe als erledigt.
    /// </summary>
    [HttpPost("tasks/{id:guid}/complete")]
    [RequireRole(UserRole.Admin, UserRole.Buero, UserRole.Mitarbeiter)]
    public async Task<ActionResult<ProjectTaskDto>> Complete(Guid id)
    {
        try
        {
            var task = await _taskService.SetCompletedAsync(id);
            return Ok(MapToDto(task));
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Setzt eine Aufgabe wieder auf offen.
    /// </summary>
    [HttpPost("tasks/{id:guid}/reopen")]
    [RequireRole(UserRole.Admin, UserRole.Buero, UserRole.Mitarbeiter)]
    public async Task<ActionResult<ProjectTaskDto>> Reopen(Guid id)
    {
        try
        {
            var task = await _taskService.SetOpenAsync(id);
            return Ok(MapToDto(task));
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Ordnet die Aufgaben eines Projekts neu an.
    /// </summary>
    [HttpPut("projects/{projectId:guid}/tasks/reorder")]
    [RequireRole(UserRole.Admin, UserRole.Buero)]
    public async Task<IActionResult> Reorder(Guid projectId, [FromBody] ReorderTasksRequest request)
    {
        try
        {
            await _taskService.ReorderAsync(projectId, request.TaskIds);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Löscht eine Aufgabe.
    /// </summary>
    [HttpDelete("tasks/{id:guid}")]
    [RequireRole(UserRole.Admin, UserRole.Buero)]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _taskService.DeleteAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    private static ProjectTaskDto MapToDto(ProjectTask task)
    {
        return new ProjectTaskDto
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Notes = task.Notes,
            Status = task.Status.ToString(),
            CreatedAt = task.CreatedAt,
            TargetDate = task.TargetDate,
            CompletedAt = task.CompletedAt,
            AssignedUserId = task.AssignedUserId,
            SortOrder = task.SortOrder
        };
    }

    private static AssignedProjectTaskDto MapToAssignedDto(ProjectTask task)
    {
        return new AssignedProjectTaskDto
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            ProjectExternalId = task.Project?.ExternalId,
            CustomerId = task.Project?.CustomerId ?? 0,
            ProjectName = task.Project?.Name ?? "Unbekanntes Projekt",
            ProjectNumber = task.Project?.ProjectNumber,
            ProjectAddress = task.Project?.Address,
            ProjectPostalCode = task.Project?.PostalCode,
            ProjectCity = task.Project?.City,
            Title = task.Title,
            Notes = task.Notes,
            Status = task.Status.ToString(),
            TargetDate = task.TargetDate,
            CompletedAt = task.CompletedAt,
            SortOrder = task.SortOrder
        };
    }
}
