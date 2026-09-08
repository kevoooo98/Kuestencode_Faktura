using FluentAssertions;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Werkbank.Acta.Data;
using Kuestencode.Werkbank.Acta.Data.Repositories;
using Kuestencode.Werkbank.Acta.Domain.Entities;
using Kuestencode.Werkbank.Acta.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Acta.Tests.Data;

public class ProjectRepositoryTests
{
    private readonly Mock<IHostApiClient> _hostApiClient = new();

    private static IDbContextFactory<ActaDbContext> CreateContextFactory() => TestDbContextFactory.CreateInMemory();

    private ProjectRepository CreateRepository(IDbContextFactory<ActaDbContext> contextFactory) =>
        new(contextFactory, _hostApiClient.Object);

    private static Project MakeProject(
        Guid? id = null, string number = "P-2026-0001", ProjectStatus status = ProjectStatus.Draft,
        int customerId = 1, int? externalId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        ProjectNumber = number,
        Name = "Projekt Beacon",
        CustomerId = customerId,
        Status = status,
        ExternalId = externalId
    };

    [Fact]
    public async Task GetByIdAsync_VorhandenesProjekt_LaedtProjektMitAufgaben()
    {
        var factory = CreateContextFactory();
        var project = MakeProject();
        project.Tasks.Add(new ProjectTask { Id = Guid.NewGuid(), ProjectId = project.Id, Title = "Aufgabe 1", SortOrder = 1 });

        await using (var context = factory.CreateDbContext())
        {
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        var result = await repository.GetByIdAsync(project.Id);

        result.Should().NotBeNull();
        result!.Tasks.Should().ContainSingle(t => t.Title == "Aufgabe 1");
    }

    [Fact]
    public async Task GetByIdAsync_UnbekannteId_GibtNullZurueck()
    {
        var repository = CreateRepository(CreateContextFactory());

        var result = await repository.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByNumberAsync_FindetProjektAnhandNummer()
    {
        var factory = CreateContextFactory();
        var project = MakeProject(number: "P-2026-0099");
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        var result = await repository.GetByNumberAsync("P-2026-0099");

        result.Should().NotBeNull();
        result!.Id.Should().Be(project.Id);
    }

    [Fact]
    public async Task GetAllAsync_OhneFilter_GibtAlleProjekteZurueck()
    {
        var factory = CreateContextFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.AddRange(
                MakeProject(number: "P-1", status: ProjectStatus.Draft, customerId: 1),
                MakeProject(number: "P-2", status: ProjectStatus.Active, customerId: 2));
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        var result = await repository.GetAllAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllAsync_MitStatusFilter_GibtNurPassendeProjekteZurueck()
    {
        var factory = CreateContextFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.AddRange(
                MakeProject(number: "P-1", status: ProjectStatus.Draft),
                MakeProject(number: "P-2", status: ProjectStatus.Active));
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        var result = await repository.GetAllAsync(status: ProjectStatus.Active);

        result.Should().ContainSingle(p => p.ProjectNumber == "P-2");
    }

    [Fact]
    public async Task GetAllAsync_MitKundenFilter_GibtNurProjekteDesKundenZurueck()
    {
        var factory = CreateContextFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.AddRange(
                MakeProject(number: "P-1", customerId: 1),
                MakeProject(number: "P-2", customerId: 2));
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        var result = await repository.GetAllAsync(customerId: 2);

        result.Should().ContainSingle(p => p.ProjectNumber == "P-2");
    }

    [Fact]
    public async Task GetByCustomerAsync_FiltertNachKunde()
    {
        var factory = CreateContextFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.AddRange(
                MakeProject(number: "P-1", customerId: 5),
                MakeProject(number: "P-2", customerId: 6));
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        var result = await repository.GetByCustomerAsync(5);

        result.Should().ContainSingle(p => p.ProjectNumber == "P-1");
    }

    [Fact]
    public async Task GetByStatusAsync_FiltertNachStatus()
    {
        var factory = CreateContextFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.AddRange(
                MakeProject(number: "P-1", status: ProjectStatus.Completed),
                MakeProject(number: "P-2", status: ProjectStatus.Draft));
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        var result = await repository.GetByStatusAsync(ProjectStatus.Completed);

        result.Should().ContainSingle(p => p.ProjectNumber == "P-1");
    }

    [Fact]
    public async Task DeleteAsync_ProjektImStatusDraft_WirdGeloescht()
    {
        var factory = CreateContextFactory();
        var project = MakeProject(status: ProjectStatus.Draft);
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        await repository.DeleteAsync(project.Id);

        (await repository.GetByIdAsync(project.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ProjektNichtImStatusDraft_WirftException()
    {
        var factory = CreateContextFactory();
        var project = MakeProject(status: ProjectStatus.Active);
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        var act = () => repository.DeleteAsync(project.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_UnbekanntesProjekt_WirftException()
    {
        var repository = CreateRepository(CreateContextFactory());

        var act = () => repository.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExistsNumberAsync_VorhandeneNummer_GibtTrueZurueck()
    {
        var factory = CreateContextFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.Add(MakeProject(number: "P-2026-0005"));
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        (await repository.ExistsNumberAsync("P-2026-0005")).Should().BeTrue();
        (await repository.ExistsNumberAsync("P-2026-9999")).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateProjectNumberAsync_OhneVorhandeneProjekte_NutztStandardformat()
    {
        _hostApiClient.Setup(h => h.GetNumberFormatSettingsAsync()).ReturnsAsync((NumberFormatSettingsDto?)null);

        var repository = CreateRepository(CreateContextFactory());
        var number = await repository.GenerateProjectNumberAsync();

        number.Should().StartWith("P-").And.EndWith("0001");
    }

    [Fact]
    public async Task GenerateProjectNumberAsync_NutztFormatAusSettings()
    {
        _hostApiClient.Setup(h => h.GetNumberFormatSettingsAsync())
            .ReturnsAsync(new NumberFormatSettingsDto { ProjectFormat = "PRJ-XXX" });

        var repository = CreateRepository(CreateContextFactory());
        var number = await repository.GenerateProjectNumberAsync();

        number.Should().Be("PRJ-001");
    }

    [Fact]
    public async Task GetNextExternalIdAsync_OhneProjekte_GibtEinsZurueck()
    {
        var repository = CreateRepository(CreateContextFactory());

        (await repository.GetNextExternalIdAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetNextExternalIdAsync_MitVorhandenenIds_GibtNaechsteFreieIdZurueck()
    {
        var factory = CreateContextFactory();
        await using (var context = factory.CreateDbContext())
        {
            context.Projects.AddRange(
                MakeProject(number: "P-1", externalId: 3),
                MakeProject(number: "P-2", externalId: 7));
            await context.SaveChangesAsync();
        }

        var repository = CreateRepository(factory);
        (await repository.GetNextExternalIdAsync()).Should().Be(8);
    }
}
