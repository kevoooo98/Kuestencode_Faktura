using FluentAssertions;
using Kuestencode.Core.Interfaces;
using Kuestencode.Core.Models;
using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Kuestencode.Faktura.Tests.Services;

public class DashboardServiceTests
{
    private readonly Mock<ICompanyService> _companyService = new();
    private readonly Mock<ICustomerService> _customerService = new();

    private static FakturaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FakturaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FakturaDbContext(options);
    }

    private DashboardService CreateService(FakturaDbContext context) =>
        new(context, _companyService.Object, _customerService.Object, NullLogger<DashboardService>.Instance);

    // ─── GetHealthAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetHealthAsync_AllesKonfiguriert_MeldetDreiGesundeItems()
    {
        _companyService.Setup(c => c.HasCompanyDataAsync()).ReturnsAsync(true);
        _companyService.Setup(c => c.IsEmailConfiguredAsync()).ReturnsAsync(true);

        using var context = CreateContext();
        var service = CreateService(context);

        var health = await service.GetHealthAsync();

        health.Should().HaveCount(3);
        health.Should().OnlyContain(h => h.IsHealthy);
        health.Single(h => h.Name == "Firmenstammdaten").StatusText.Should().Be("Konfiguriert");
        health.Single(h => h.Name == "E-Mail-Versand").StatusText.Should().Be("Konfiguriert");
        health.Single(h => h.Name == "PDF-Erstellung").StatusText.Should().Be("OK");
    }

    [Fact]
    public async Task GetHealthAsync_NichtKonfiguriert_MeldetEntsprechendeDetailMessages()
    {
        _companyService.Setup(c => c.HasCompanyDataAsync()).ReturnsAsync(false);
        _companyService.Setup(c => c.IsEmailConfiguredAsync()).ReturnsAsync(false);

        using var context = CreateContext();
        var service = CreateService(context);

        var health = await service.GetHealthAsync();

        var company = health.Single(h => h.Name == "Firmenstammdaten");
        company.IsHealthy.Should().BeFalse();
        company.DetailMessage.Should().Contain("Firmenstammdaten");

        var email = health.Single(h => h.Name == "E-Mail-Versand");
        email.IsHealthy.Should().BeFalse();
        email.DetailMessage.Should().Contain("E-Mail-Versand");
    }

    [Fact]
    public async Task GetHealthAsync_CompanyServiceWirftException_FaengtFehlerAbUndMeldetFehlerStatus()
    {
        _companyService.Setup(c => c.HasCompanyDataAsync()).ThrowsAsync(new InvalidOperationException("boom"));
        _companyService.Setup(c => c.IsEmailConfiguredAsync()).ReturnsAsync(true);

        using var context = CreateContext();
        var service = CreateService(context);

        var health = await service.GetHealthAsync();

        var company = health.Single(h => h.Name == "Firmenstammdaten");
        company.IsHealthy.Should().BeFalse();
        company.StatusText.Should().Be("Fehler");
    }

    // ─── GetSummaryAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummaryAsync_ZaehltRechnungenNachStatusUndSummiertUmsatzDesMonats()
    {
        using var context = CreateContext();
        var now = DateTime.UtcNow;
        var firstOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        context.Invoices.AddRange(
            MakeInvoice(1, InvoiceStatus.Sent),
            MakeInvoice(2, InvoiceStatus.Sent),
            MakeInvoice(3, InvoiceStatus.Overdue),
            MakeInvoice(4, InvoiceStatus.Draft),
            MakeInvoice(5, InvoiceStatus.Paid, paidDate: firstOfMonth.AddDays(1), items: [MakeItem(2, 100)]),
            MakeInvoice(6, InvoiceStatus.Paid, paidDate: firstOfMonth.AddMonths(-1), items: [MakeItem(1, 500)]) // Vormonat, zählt nicht
        );
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var summary = await service.GetSummaryAsync();

        summary.OpenInvoices.Should().Be(2);
        summary.OverdueInvoices.Should().Be(1);
        summary.DraftInvoices.Should().Be(1);
        summary.RevenueThisMonth.Should().Be(238m); // 2 * 100 * 1.19
    }

    [Fact]
    public async Task GetSummaryAsync_KeineRechnungen_GibtNullenZurueck()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var summary = await service.GetSummaryAsync();

        summary.OpenInvoices.Should().Be(0);
        summary.OverdueInvoices.Should().Be(0);
        summary.DraftInvoices.Should().Be(0);
        summary.RevenueThisMonth.Should().Be(0);
    }

    // ─── GetRecentActivitiesAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetRecentActivitiesAsync_ErzeugtTextJeStatus()
    {
        using var context = CreateContext();
        var baseDate = DateTime.UtcNow;

        context.Invoices.AddRange(
            MakeInvoice(1, InvoiceStatus.Sent, updatedAt: baseDate.AddMinutes(-1)),
            MakeInvoice(2, InvoiceStatus.Paid, updatedAt: baseDate.AddMinutes(-2)),
            MakeInvoice(3, InvoiceStatus.Draft, updatedAt: baseDate.AddMinutes(-3)),
            MakeInvoice(4, InvoiceStatus.Cancelled, updatedAt: baseDate.AddMinutes(-4))
        );
        await context.SaveChangesAsync();

        _customerService.Setup(c => c.GetAllAsync()).ReturnsAsync(new List<Customer>());

        var service = CreateService(context);

        var activities = await service.GetRecentActivitiesAsync(take: 4);

        activities.Should().HaveCount(4);
        activities.Should().Contain(a => a.Text.Contains("versendet"));
        activities.Should().Contain(a => a.Text.Contains("beglichen"));
        activities.Should().Contain(a => a.Text.Contains("als Entwurf erstellt"));
        activities.Should().Contain(a => a.Text.Contains("storniert"));
    }

    [Fact]
    public async Task GetRecentActivitiesAsync_FuelltMitKundenAuf_WennWenigerRechnungenAlsTake()
    {
        using var context = CreateContext();
        context.Invoices.Add(MakeInvoice(1, InvoiceStatus.Sent, updatedAt: DateTime.UtcNow));
        await context.SaveChangesAsync();

        _customerService.Setup(c => c.GetAllAsync()).ReturnsAsync(new List<Customer>
        {
            new() { Id = 1, Name = "Nordlicht Media", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = 2, Name = "Seewind IT", CreatedAt = DateTime.UtcNow.AddDays(-2) }
        });

        var service = CreateService(context);

        var activities = await service.GetRecentActivitiesAsync(take: 3);

        activities.Should().HaveCount(3);
        activities.Should().Contain(a => a.Text.Contains("Nordlicht Media"));
    }

    [Fact]
    public async Task GetRecentActivitiesAsync_CustomerServiceWirftException_GibtLeereListeZurueck()
    {
        using var context = CreateContext();
        context.Invoices.Add(MakeInvoice(1, InvoiceStatus.Sent, updatedAt: DateTime.UtcNow));
        await context.SaveChangesAsync();

        _customerService.Setup(c => c.GetAllAsync()).ThrowsAsync(new InvalidOperationException("boom"));

        var service = CreateService(context);

        var activities = await service.GetRecentActivitiesAsync(take: 5);

        activities.Should().BeEmpty();
    }

    private static Invoice MakeInvoice(
        int id,
        InvoiceStatus status,
        DateTime? paidDate = null,
        DateTime? updatedAt = null,
        List<InvoiceItem>? items = null) =>
        new()
        {
            Id = id,
            InvoiceNumber = $"R-2026-{id:D4}",
            InvoiceDate = DateTime.UtcNow,
            CustomerId = 1,
            Status = status,
            PaidDate = paidDate,
            UpdatedAt = updatedAt ?? DateTime.UtcNow,
            Items = items ?? []
        };

    private static InvoiceItem MakeItem(decimal qty, decimal price, decimal vat = 19m) =>
        new() { Quantity = qty, UnitPrice = price, VatRate = vat };
}
