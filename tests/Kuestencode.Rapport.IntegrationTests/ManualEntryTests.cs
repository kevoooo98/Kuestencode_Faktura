using FluentAssertions;
using Kuestencode.Core.Models;
using Kuestencode.Rapport.Services;
using Kuestencode.Rapport.IntegrationTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;

namespace Kuestencode.Rapport.IntegrationTests;

public class ManualEntryTests : IClassFixture<RapportWebApplicationFactory>
{
    private readonly RapportWebApplicationFactory _factory;

    public ManualEntryTests(RapportWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ManualEntry_CreateUpdateDelete_ShouldWork()
    {
        using var scope = _factory.Services.CreateScope();
        var customerService = scope.ServiceProvider.GetRequiredService<TestCustomerService>();
        var timeEntryService = scope.ServiceProvider.GetRequiredService<TimeEntryService>();

        var customer = await customerService.CreateAsync(new Customer { Name = "Beta AG", CustomerNumber = "C-2002" });

        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow.AddHours(-1);
        var entry = await timeEntryService.CreateManualEntryAsync(start, end, null, customer.Id, "Analyse");
        entry.IsManual.Should().BeTrue();

        var updated = await timeEntryService.UpdateEntryAsync(entry.Id, start.AddMinutes(-5), end.AddMinutes(10), null, customer.Id, "Analyse v2");
        updated.Description.Should().Be("Analyse v2");

        await timeEntryService.SoftDeleteEntryAsync(entry.Id);

        // RapportDbContext hat einen globalen Query-Filter (!IsDeleted) auf TimeEntry,
        // daher blendet auch GetEntryAsync weich gelöschte Einträge aus.
        var fetched = await timeEntryService.GetEntryAsync(entry.Id);
        fetched.Should().BeNull();
    }
}
