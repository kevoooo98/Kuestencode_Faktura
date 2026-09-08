using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Kuestencode.Core.Interfaces;
using Kuestencode.Core.Models;
using Kuestencode.Rapport.Data;
using Kuestencode.Rapport.Data.Repositories;
using Kuestencode.Rapport.Models;
using Kuestencode.Rapport.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Kuestencode.Rapport.Tests.Services;

public class TimerServiceTests
{
    [Fact]
    public async Task StartTimerAsync_WithProject_UsesProjectCustomer()
    {
        var repository = new TimeEntryRepository(CreateContextFactory());
        var projectService = new Mock<IProjectService>();
        var customerService = new Mock<ICustomerService>();

        var project = new TestProject
        {
            Id = 42,
            Name = "Project Beacon",
            ProjectNumber = "PR-42",
            CustomerId = 7,
            CustomerName = "Nordlicht Media"
        };

        projectService.Setup(p => p.GetProjectByIdAsync(42))
            .ReturnsAsync(project);
        customerService.Setup(c => c.GetByIdAsync(7))
            .ReturnsAsync(new Customer { Id = 7, Name = "Nordlicht Media" });

        var service = CreateTimerService(repository, projectService.Object, customerService.Object);

        var entry = await service.StartTimerAsync(42, 999, "Working on feature");

        entry.CustomerId.Should().Be(7);
        entry.CustomerName.Should().Be("Nordlicht Media");
        entry.ProjectId.Should().Be(42);
        entry.ProjectName.Should().Be("Project Beacon");
        customerService.Verify(c => c.GetByIdAsync(7), Times.Once);
    }

    [Fact]
    public async Task StartTimerAsync_WithoutProject_RequiresCustomerId()
    {
        var repository = new TimeEntryRepository(CreateContextFactory());
        var projectService = new Mock<IProjectService>();
        var customerService = new Mock<ICustomerService>();

        customerService.Setup(c => c.GetByIdAsync(1))
            .ReturnsAsync(new Customer { Id = 1, Name = "Kuestencode GmbH" });

        var service = CreateTimerService(repository, projectService.Object, customerService.Object);

        var entry = await service.StartTimerAsync(null, 1, "Support call");

        entry.CustomerId.Should().Be(1);
        entry.CustomerName.Should().Be("Kuestencode GmbH");
        entry.ProjectId.Should().BeNull();
    }

    [Fact]
    public async Task StartTimerAsync_WithoutProjectAndCustomer_ThrowsValidationException()
    {
        var repository = new TimeEntryRepository(CreateContextFactory());
        var projectService = new Mock<IProjectService>();
        var customerService = new Mock<ICustomerService>();

        var service = CreateTimerService(repository, projectService.Object, customerService.Object);

        var act = () => service.StartTimerAsync(null, null, "Missing customer");

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*CustomerId is required when no project is selected*");
    }

    [Fact]
    public async Task StartTimerAsync_WhenTimerAlreadyRunning_ThrowsValidationException()
    {
        var repository = new TimeEntryRepository(CreateContextFactory());
        var projectService = new Mock<IProjectService>();
        var customerService = new Mock<ICustomerService>();

        customerService.Setup(c => c.GetByIdAsync(1))
            .ReturnsAsync(new Customer { Id = 1, Name = "Kuestencode GmbH" });

        var service = CreateTimerService(repository, projectService.Object, customerService.Object);

        await service.StartTimerAsync(null, 1, "First timer");

        var act = () => service.StartTimerAsync(null, 1, "Second timer");

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*already running*");
    }

    [Fact]
    public async Task GetCurrentDurationAsync_ReturnsElapsedTime()
    {
        var contextFactory = CreateContextFactory();

        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            context.TimeEntries.Add(new TimeEntry
            {
                StartTime = DateTime.UtcNow.AddMinutes(-10),
                Status = TimeEntryStatus.Running,
                CustomerId = 1,
                CustomerName = "Kuestencode GmbH"
            });
            await context.SaveChangesAsync();
        }

        var repository = new TimeEntryRepository(contextFactory);
        var projectService = new Mock<IProjectService>();
        var customerService = new Mock<ICustomerService>();

        var service = CreateTimerService(repository, projectService.Object, customerService.Object);

        var duration = await service.GetCurrentDurationAsync();

        duration.Should().BeGreaterOrEqualTo(TimeSpan.FromMinutes(9));
    }

    [Fact]
    public async Task StartTimerAsync_CachesProjectName_WhenProjectProvided()
    {
        var repository = new TimeEntryRepository(CreateContextFactory());
        var projectService = new Mock<IProjectService>();
        var customerService = new Mock<ICustomerService>();

        var project = new TestProject
        {
            Id = 5,
            Name = "Project Aurora",
            ProjectNumber = "PA-5",
            CustomerId = 2,
            CustomerName = "Seewind IT"
        };

        projectService.Setup(p => p.GetProjectByIdAsync(5))
            .ReturnsAsync(project);
        customerService.Setup(c => c.GetByIdAsync(2))
            .ReturnsAsync(new Customer { Id = 2, Name = "Seewind IT" });

        var service = CreateTimerService(repository, projectService.Object, customerService.Object);

        var entry = await service.StartTimerAsync(5, null, "Project work");

        entry.ProjectName.Should().Be("Project Aurora");
        entry.CustomerName.Should().Be("Seewind IT");
    }

    private static TimerService CreateTimerService(
        TimeEntryRepository repository,
        IProjectService projectService,
        ICustomerService customerService)
    {
        var settingsService = new SettingsService(CreateContextFactory(), NullLogger<SettingsService>.Instance);
        return new TimerService(repository, projectService, customerService, settingsService, new TimeRoundingService());
    }

    private static IDbContextFactory<RapportDbContext> CreateContextFactory()
    {
        var options = new DbContextOptionsBuilder<RapportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContextFactory(options);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<RapportDbContext>
    {
        private readonly DbContextOptions<RapportDbContext> _options;

        public TestDbContextFactory(DbContextOptions<RapportDbContext> options)
        {
            _options = options;
        }

        public RapportDbContext CreateDbContext() => new(_options);
    }

    private class TestProject : IProject
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? ProjectNumber { get; init; }
        public int CustomerId { get; init; }
        public string CustomerName { get; init; } = string.Empty;
    }
}
