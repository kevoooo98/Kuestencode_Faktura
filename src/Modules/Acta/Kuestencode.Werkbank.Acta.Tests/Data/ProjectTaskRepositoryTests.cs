using FluentAssertions;
using Kuestencode.Werkbank.Acta.Data;
using Kuestencode.Werkbank.Acta.Data.Repositories;
using Kuestencode.Werkbank.Acta.Domain.Entities;
using Kuestencode.Werkbank.Acta.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kuestencode.Werkbank.Acta.Tests.Data;

public class ProjectTaskRepositoryTests
{
    private static IDbContextFactory<ActaDbContext> CreateContextFactory() => TestDbContextFactory.CreateInMemory();

    private static Project MakeProject() => new()
    {
        Id = Guid.NewGuid(),
        ProjectNumber = "P-2026-0001",
        Name = "Projekt Beacon",
        CustomerId = 1
    };

    private static ProjectTask MakeTask(Guid projectId, string title, int sortOrder = 0,
        Guid? assignedUserId = null, ProjectTaskStatus status = ProjectTaskStatus.Open, DateOnly? targetDate = null) => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Title = title,
        SortOrder = sortOrder,
        AssignedUserId = assignedUserId,
        Status = status,
        TargetDate = targetDate
    };

    [Fact]
    public async Task GetByIdAsync_LaedtAufgabeMitProjekt()
    {
        var factory = CreateContextFactory();
        var project = MakeProject();
        var task = MakeTask(project.Id, "Design");
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.Add(project);
            context.Tasks.Add(task);
            await context.SaveChangesAsync();
        }

        var repository = new ProjectTaskRepository(factory);
        var result = await repository.GetByIdAsync(task.Id);

        result.Should().NotBeNull();
        result!.Project.Id.Should().Be(project.Id);
    }

    [Fact]
    public async Task GetByProjectIdAsync_GibtAufgabenSortiertNachSortOrderZurueck()
    {
        var factory = CreateContextFactory();
        var project = MakeProject();
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.Add(project);
            context.Tasks.AddRange(
                MakeTask(project.Id, "Zweite", sortOrder: 2),
                MakeTask(project.Id, "Erste", sortOrder: 1));
            await context.SaveChangesAsync();
        }

        var repository = new ProjectTaskRepository(factory);
        var result = await repository.GetByProjectIdAsync(project.Id);

        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Erste");
        result[1].Title.Should().Be("Zweite");
    }

    [Fact]
    public async Task GetByAssignedUserIdAsync_FiltertNachZugewiesenemUser()
    {
        var factory = CreateContextFactory();
        var project = MakeProject();
        var userId = Guid.NewGuid();
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.Add(project);
            context.Tasks.AddRange(
                MakeTask(project.Id, "Meine Aufgabe", assignedUserId: userId),
                MakeTask(project.Id, "Andere Aufgabe", assignedUserId: Guid.NewGuid()));
            await context.SaveChangesAsync();
        }

        var repository = new ProjectTaskRepository(factory);
        var result = await repository.GetByAssignedUserIdAsync(userId);

        result.Should().ContainSingle(t => t.Title == "Meine Aufgabe");
    }

    [Fact]
    public async Task DeleteAsync_VorhandeneAufgabe_WirdEntfernt()
    {
        var factory = CreateContextFactory();
        var project = MakeProject();
        var task = MakeTask(project.Id, "Zu löschen");
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.Add(project);
            context.Tasks.Add(task);
            await context.SaveChangesAsync();
        }

        var repository = new ProjectTaskRepository(factory);
        await repository.DeleteAsync(task.Id);

        (await repository.GetByIdAsync(task.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_UnbekannteAufgabe_WirftException()
    {
        var repository = new ProjectTaskRepository(CreateContextFactory());

        var act = () => repository.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetNextSortOrderAsync_OhneAufgaben_GibtEinsZurueck()
    {
        var repository = new ProjectTaskRepository(CreateContextFactory());

        (await repository.GetNextSortOrderAsync(Guid.NewGuid())).Should().Be(1);
    }

    [Fact]
    public async Task GetNextSortOrderAsync_MitVorhandenenAufgaben_GibtNaechstenWertZurueck()
    {
        var factory = CreateContextFactory();
        var project = MakeProject();
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.Add(project);
            context.Tasks.AddRange(
                MakeTask(project.Id, "A", sortOrder: 1),
                MakeTask(project.Id, "B", sortOrder: 4));
            await context.SaveChangesAsync();
        }

        var repository = new ProjectTaskRepository(factory);
        (await repository.GetNextSortOrderAsync(project.Id)).Should().Be(5);
    }
}
