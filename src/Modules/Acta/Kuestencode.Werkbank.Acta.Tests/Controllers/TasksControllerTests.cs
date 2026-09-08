using FluentAssertions;
using Kuestencode.Werkbank.Acta.Controllers;
using Kuestencode.Werkbank.Acta.Controllers.Dtos;
using Kuestencode.Werkbank.Acta.Domain.Dtos;
using Kuestencode.Werkbank.Acta.Domain.Entities;
using Kuestencode.Werkbank.Acta.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Acta.Tests.Controllers;

public class TasksControllerTests
{
    private readonly Mock<IProjectTaskService> _taskService = new();

    private TasksController CreateController() => new(_taskService.Object, Mock.Of<ILogger<TasksController>>());

    private static ProjectTask MakeTask(Guid? id = null, Guid? projectId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        ProjectId = projectId ?? Guid.NewGuid(),
        Title = "Aufgabe"
    };

    // ─── GetByProject ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByProject_GibtAlleAufgabenZurueck()
    {
        var projectId = Guid.NewGuid();
        _taskService.Setup(s => s.GetByProjectIdAsync(projectId)).ReturnsAsync([MakeTask(projectId: projectId)]);

        var result = await CreateController().GetByProject(projectId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<List<ProjectTaskDto>>().Subject.Should().HaveCount(1);
    }

    // ─── GetAssignedToUser ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAssignedToUser_MapptProjektAngaben()
    {
        var userId = Guid.NewGuid();
        var task = MakeTask();
        task.Project = new Project { Id = task.ProjectId, ExternalId = 7, CustomerId = 5, Name = "Testprojekt", ProjectNumber = "P-1" };
        _taskService.Setup(s => s.GetByAssignedUserIdAsync(userId)).ReturnsAsync([task]);

        var result = await CreateController().GetAssignedToUser(userId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<List<AssignedProjectTaskDto>>().Subject;
        dtos.Should().ContainSingle();
        dtos[0].ProjectExternalId.Should().Be(7);
        dtos[0].ProjectName.Should().Be("Testprojekt");
    }

    [Fact]
    public async Task GetAssignedToUser_OhneProjektNavigation_VerwendetFallbackWerte()
    {
        var userId = Guid.NewGuid();
        _taskService.Setup(s => s.GetByAssignedUserIdAsync(userId)).ReturnsAsync([MakeTask()]);

        var result = await CreateController().GetAssignedToUser(userId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<List<AssignedProjectTaskDto>>().Subject;
        dtos[0].ProjectName.Should().Be("Unbekanntes Projekt");
    }

    // ─── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Erfolgreich_GibtCreatedAtActionZurueck()
    {
        var projectId = Guid.NewGuid();
        _taskService.Setup(s => s.CreateAsync(projectId, It.IsAny<CreateProjectTaskDto>())).ReturnsAsync(MakeTask(projectId: projectId));

        var result = await CreateController().Create(projectId, new CreateTaskRequest { Title = "Neu" });

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task Create_ProjektNichtGefunden_GibtNotFoundZurueck()
    {
        _taskService.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateProjectTaskDto>()))
            .ThrowsAsync(new InvalidOperationException("Projekt nicht gefunden"));

        var result = await CreateController().Create(Guid.NewGuid(), new CreateTaskRequest());

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─── GetById ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_UnbekannteId_GibtNotFoundZurueck()
    {
        _taskService.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((ProjectTask?)null);

        var result = await CreateController().GetById(Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Erfolgreich_GibtOkZurueck()
    {
        var task = MakeTask();
        _taskService.Setup(s => s.UpdateAsync(task.Id, It.IsAny<UpdateProjectTaskDto>())).ReturnsAsync(task);

        var result = await CreateController().Update(task.Id, new UpdateTaskRequest { Title = "Geändert" });

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Update_UnbekannteId_GibtNotFoundZurueck()
    {
        _taskService.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateProjectTaskDto>()))
            .ThrowsAsync(new InvalidOperationException("Aufgabe nicht gefunden"));

        var result = await CreateController().Update(Guid.NewGuid(), new UpdateTaskRequest());

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─── Complete / Reopen ───────────────────────────────────────────────────

    [Fact]
    public async Task Complete_Erfolgreich_GibtOkZurueck()
    {
        var task = MakeTask();
        _taskService.Setup(s => s.SetCompletedAsync(task.Id)).ReturnsAsync(task);

        var result = await CreateController().Complete(task.Id);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Reopen_Erfolgreich_GibtOkZurueck()
    {
        var task = MakeTask();
        _taskService.Setup(s => s.SetOpenAsync(task.Id)).ReturnsAsync(task);

        var result = await CreateController().Reopen(task.Id);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Complete_UnbekannteId_GibtNotFoundZurueck()
    {
        _taskService.Setup(s => s.SetCompletedAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new InvalidOperationException("Aufgabe nicht gefunden"));

        var result = await CreateController().Complete(Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─── Reorder ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reorder_Erfolgreich_GibtNoContentZurueck()
    {
        var projectId = Guid.NewGuid();
        var taskIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var result = await CreateController().Reorder(projectId, new ReorderTasksRequest { TaskIds = taskIds });

        result.Should().BeOfType<NoContentResult>();
        _taskService.Verify(s => s.ReorderAsync(projectId, taskIds), Times.Once);
    }

    [Fact]
    public async Task Reorder_ProjektNichtGefunden_GibtNotFoundZurueck()
    {
        _taskService.Setup(s => s.ReorderAsync(It.IsAny<Guid>(), It.IsAny<List<Guid>>()))
            .ThrowsAsync(new InvalidOperationException("Projekt nicht gefunden"));

        var result = await CreateController().Reorder(Guid.NewGuid(), new ReorderTasksRequest());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ─── Delete ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Erfolgreich_GibtNoContentZurueck()
    {
        var result = await CreateController().Delete(Guid.NewGuid());

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_UnbekannteId_GibtNotFoundZurueck()
    {
        _taskService.Setup(s => s.DeleteAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new InvalidOperationException("Aufgabe nicht gefunden"));

        var result = await CreateController().Delete(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
