using InvoiceApp.Data;
using InvoiceApp.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Services;

public class InvoiceOverdueService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InvoiceOverdueService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1); // Prüft stündlich

    public InvoiceOverdueService(
        IServiceProvider serviceProvider,
        ILogger<InvoiceOverdueService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Invoice Overdue Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndUpdateOverdueInvoicesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking overdue invoices");
            }

            // Warte bis zum nächsten Check
            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Invoice Overdue Service stopped");
    }

    private async Task CheckAndUpdateOverdueInvoicesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;

        // Finde alle Fakturen, die:
        // 1. Status "Sent" haben
        // 2. Ein Fälligkeitsdatum haben
        // 3. Das Fälligkeitsdatum überschritten ist
        var overdueInvoices = await context.Invoices
            .Where(i => i.Status == InvoiceStatus.Sent &&
                       i.DueDate.HasValue &&
                       i.DueDate.Value < now)
            .ToListAsync();

        if (overdueInvoices.Any())
        {
            _logger.LogInformation("Found {Count} overdue invoices", overdueInvoices.Count);

            foreach (var invoice in overdueInvoices)
            {
                invoice.Status = InvoiceStatus.Overdue;
                _logger.LogInformation("Marked invoice {InvoiceNumber} as overdue (Due: {DueDate})",
                    invoice.InvoiceNumber,
                    invoice.DueDate);
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Updated {Count} invoices to overdue status", overdueInvoices.Count);
        }
    }
}
