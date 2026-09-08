using FluentAssertions;
using Kuestencode.Core.Interfaces;
using Kuestencode.Core.Models;
using Kuestencode.Werkbank.Acta.Controllers;
using Kuestencode.Werkbank.Acta.Controllers.Dtos;
using Kuestencode.Werkbank.Acta.Domain.Dtos;
using Kuestencode.Werkbank.Acta.Domain.Entities;
using Kuestencode.Werkbank.Acta.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Acta.Tests.Controllers;

public class ProjectsControllerTests
{
    private readonly Mock<Kuestencode.Werkbank.Acta.Services.IProjectService> _projectService = new();
    private readonly Mock<ICustomerService> _customerService = new();

    public ProjectsControllerTests()
    {
        _projectService.Setup(s => s.GetAvailableTransitions(It.IsAny<Project>()))
            .Returns(new List<(ProjectStatus, string)>());
    }

    private ProjectsController CreateController() => new(_projectService.Object, _customerService.Object, Mock.Of<ILogger<ProjectsController>>());

    private static Project MakeProject(Guid? id = null, ProjectStatus status = ProjectStatus.Draft) => new()
    {
        Id = id ?? Guid.NewGuid(),
        ProjectNumber = "P-0001",
        Name = "Testprojekt",
        CustomerId = 1,
        Status = status
    };

    // ─── GetAll ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_OhneFilter_GibtAlleProjekteZurueck()
    {
        _projectService.Setup(s => s.GetAllAsync(It.IsAny<ProjectFilterDto>())).ReturnsAsync([MakeProject()]);

        var result = await CreateController().GetAll();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<List<ProjectDto>>().Subject.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAll_MitGueltigemStatusFilter_WirdAnServiceWeitergegeben()
    {
        _projectService.Setup(s => s.GetAllAsync(It.Is<ProjectFilterDto>(f => f.Status == ProjectStatus.Active)))
            .ReturnsAsync([]);

        await CreateController().GetAll(status: "Active");

        _projectService.Verify(s => s.GetAllAsync(It.Is<ProjectFilterDto>(f => f.Status == ProjectStatus.Active)), Times.Once);
    }

    [Fact]
    public async Task GetAll_MitUngueltigemStatusFilter_IgnoriertDenFilter()
    {
        _projectService.Setup(s => s.GetAllAsync(It.Is<ProjectFilterDto>(f => f.Status == null))).ReturnsAsync([]);

        await CreateController().GetAll(status: "NichtExistent");

        _projectService.Verify(s => s.GetAllAsync(It.Is<ProjectFilterDto>(f => f.Status == null)), Times.Once);
    }

    // ─── GetById ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_VorhandenesProjekt_GibtOkZurueck()
    {
        var project = MakeProject();
        _projectService.Setup(s => s.GetByIdAsync(project.Id)).ReturnsAsync(project);

        var result = await CreateController().GetById(project.Id);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetById_UnbekanntesProjekt_GibtNotFoundZurueck()
    {
        _projectService.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Project?)null);

        var result = await CreateController().GetById(Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // ─── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Erfolgreich_GibtCreatedAtActionZurueck()
    {
        var project = MakeProject();
        _projectService.Setup(s => s.CreateAsync(It.IsAny<CreateProjectDto>())).ReturnsAsync(project);

        var result = await CreateController().Create(new CreateProjectRequest { Name = "Neu", CustomerId = 1 });

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    [Fact]
    public async Task Create_ServiceWirftInvalidOperationException_GibtBadRequestZurueck()
    {
        _projectService.Setup(s => s.CreateAsync(It.IsAny<CreateProjectDto>()))
            .ThrowsAsync(new InvalidOperationException("Projektnummer bereits vergeben"));

        var result = await CreateController().Create(new CreateProjectRequest());

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ProjektNichtGefunden_GibtNotFoundZurueck()
    {
        _projectService.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateProjectDto>()))
            .ThrowsAsync(new InvalidOperationException("Projekt nicht gefunden"));

        var result = await CreateController().Update(Guid.NewGuid(), new UpdateProjectRequest());

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Update_AndereValidierungsfehler_GibtBadRequestZurueck()
    {
        _projectService.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateProjectDto>()))
            .ThrowsAsync(new InvalidOperationException("Ungültige Daten"));

        var result = await CreateController().Update(Guid.NewGuid(), new UpdateProjectRequest());

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ─── ChangeStatus ────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeStatus_UngueltigerStatuswert_GibtBadRequestZurueck()
    {
        var result = await CreateController().ChangeStatus(Guid.NewGuid(), new ChangeStatusRequest { NewStatus = "Unbekannt" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ChangeStatus_GueltigerUebergang_GibtOkZurueck()
    {
        var project = MakeProject(status: ProjectStatus.Active);
        _projectService.Setup(s => s.ChangeStatusAsync(project.Id, ProjectStatus.Active)).ReturnsAsync(project);

        var result = await CreateController().ChangeStatus(project.Id, new ChangeStatusRequest { NewStatus = "Active" });

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    // ─── Delete ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Erfolgreich_GibtNoContentZurueck()
    {
        var result = await CreateController().Delete(Guid.NewGuid());

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_ProjektNichtImDraftStatus_GibtBadRequestZurueck()
    {
        _projectService.Setup(s => s.DeleteAsync(It.IsAny<Guid>()))
            .ThrowsAsync(new InvalidOperationException("Nur Projekte im Entwurf können gelöscht werden"));

        var result = await CreateController().Delete(Guid.NewGuid());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ─── GetSummary ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_UnbekanntesProjekt_GibtNotFoundZurueck()
    {
        _projectService.Setup(s => s.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Project?)null);

        var result = await CreateController().GetSummary(Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetSummary_VorhandenesProjekt_GibtBasisdatenZurueck()
    {
        var project = MakeProject();
        project.BudgetNet = 5000m;
        _projectService.Setup(s => s.GetByIdAsync(project.Id)).ReturnsAsync(project);

        var result = await CreateController().GetSummary(project.Id);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<ProjectSummaryDto>().Subject;
        summary.BudgetNet.Should().Be(5000m);
    }

    // ─── GetExternalProjects ─────────────────────────────────────────────────

    [Fact]
    public async Task GetExternalProjects_FiltertDraftUndArchivedUndOhneExternalId()
    {
        var mitExternalId = MakeProject(status: ProjectStatus.Active);
        mitExternalId.ExternalId = 42;
        var draftMitExternalId = MakeProject(status: ProjectStatus.Draft);
        draftMitExternalId.ExternalId = 43;
        var ohneExternalId = MakeProject(status: ProjectStatus.Active);

        _projectService.Setup(s => s.GetAllAsync(null)).ReturnsAsync([mitExternalId, draftMitExternalId, ohneExternalId]);
        _customerService.Setup(c => c.GetByIdAsync(mitExternalId.CustomerId)).ReturnsAsync(new Customer { Id = 1, Name = "Kunde AG" });

        var result = await CreateController().GetExternalProjects();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var projects = ok.Value.Should().BeAssignableTo<List<Kuestencode.Shared.Contracts.Acta.ActaProjectDto>>().Subject;
        projects.Should().ContainSingle();
        projects[0].CustomerName.Should().Be("Kunde AG");
        _projectService.Verify(s => s.EnsureExternalIdsAsync(), Times.Once);
    }

    // ─── GetExternalProject ──────────────────────────────────────────────────

    [Fact]
    public async Task GetExternalProject_UnbekannteExternalId_GibtNotFoundZurueck()
    {
        _projectService.Setup(s => s.GetAllAsync(null)).ReturnsAsync([]);

        var result = await CreateController().GetExternalProject(999);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetExternalProject_BekannteExternalId_GibtProjektZurueck()
    {
        var project = MakeProject();
        project.ExternalId = 42;
        _projectService.Setup(s => s.GetAllAsync(null)).ReturnsAsync([project]);
        _customerService.Setup(c => c.GetByIdAsync(project.CustomerId)).ReturnsAsync((Customer?)null);

        var result = await CreateController().GetExternalProject(42);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<Kuestencode.Shared.Contracts.Acta.ActaProjectDto>().Subject;
        dto.CustomerName.Should().Be("Unbekannt");
    }
}
