using FluentAssertions;
using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kuestencode.Faktura.Tests.Services;

public class InvoiceOverdueServiceTests
{
    private static ServiceProvider CreateProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FakturaDbContext>(o => o.UseInMemoryDatabase(dbName));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ExecuteAsync_MarkiertUeberfaelligeGesendeteRechnungenAlsOverdue()
    {
        var dbName = Guid.NewGuid().ToString();
        var provider = CreateProvider(dbName);

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FakturaDbContext>();
            context.Invoices.AddRange(
                new Invoice
                {
                    Id = 1,
                    InvoiceNumber = "R-0001",
                    InvoiceDate = DateTime.UtcNow.AddDays(-30),
                    CustomerId = 1,
                    Status = InvoiceStatus.Sent,
                    DueDate = DateTime.UtcNow.AddDays(-1) // Überfällig
                },
                new Invoice
                {
                    Id = 2,
                    InvoiceNumber = "R-0002",
                    InvoiceDate = DateTime.UtcNow,
                    CustomerId = 1,
                    Status = InvoiceStatus.Sent,
                    DueDate = DateTime.UtcNow.AddDays(14) // Noch nicht überfällig
                },
                new Invoice
                {
                    Id = 3,
                    InvoiceNumber = "R-0003",
                    InvoiceDate = DateTime.UtcNow.AddDays(-30),
                    CustomerId = 1,
                    Status = InvoiceStatus.Draft,
                    DueDate = DateTime.UtcNow.AddDays(-1) // Überfällig, aber noch Entwurf
                });
            await context.SaveChangesAsync();
        }

        var service = new InvoiceOverdueService(provider, NullLogger<InvoiceOverdueService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<FakturaDbContext>();

        (await verifyContext.Invoices.FindAsync(1))!.Status.Should().Be(InvoiceStatus.Overdue);
        (await verifyContext.Invoices.FindAsync(2))!.Status.Should().Be(InvoiceStatus.Sent);
        (await verifyContext.Invoices.FindAsync(3))!.Status.Should().Be(InvoiceStatus.Draft);
    }
}
