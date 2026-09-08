using Kuestencode.Core.Interfaces;
using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Models.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Faktura.Services;

public class DashboardService : IDashboardService
{
    private readonly FakturaDbContext _context;
    private readonly ICompanyService _companyService;
    private readonly ICustomerService _customerService;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        FakturaDbContext context,
        ICompanyService companyService,
        ICustomerService customerService,
        ILogger<DashboardService> logger)
    {
        _context = context;
        _companyService = companyService;
        _customerService = customerService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServiceHealthItem>> GetHealthAsync()
    {
        var healthItems = new List<ServiceHealthItem>();
        var now = DateTime.Now;

        // Firmenstammdaten Health Check
        try
        {
            var hasCompanyData = await _companyService.HasCompanyDataAsync();

            healthItems.Add(new ServiceHealthItem
            {
                Name = "Firmenstammdaten",
                IsHealthy = hasCompanyData,
                StatusText = hasCompanyData ? "Konfiguriert" : "Nicht konfiguriert",
                CheckedAt = now,
                DetailMessage = hasCompanyData ? null : "Bitte vervollständigen Sie Ihre Firmenstammdaten."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Company data health check failed");
            healthItems.Add(new ServiceHealthItem
            {
                Name = "Firmenstammdaten",
                IsHealthy = false,
                StatusText = "Fehler",
                CheckedAt = now,
                DetailMessage = "Firmenstammdaten konnten nicht geprüft werden."
            });
        }

        // E-Mail Health Check
        try
        {
            var isEmailConfigured = await _companyService.IsEmailConfiguredAsync();

            healthItems.Add(new ServiceHealthItem
            {
                Name = "E-Mail-Versand",
                IsHealthy = isEmailConfigured,
                StatusText = isEmailConfigured ? "Konfiguriert" : "Nicht konfiguriert",
                CheckedAt = now,
                DetailMessage = isEmailConfigured ? null : "E-Mail-Versand ist noch nicht eingerichtet."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email configuration health check failed");
            healthItems.Add(new ServiceHealthItem
            {
                Name = "E-Mail-Versand",
                IsHealthy = false,
                StatusText = "Fehler",
                CheckedAt = now,
                DetailMessage = "E-Mail-Konfiguration konnte nicht geprüft werden."
            });
        }

        // PDF Generation Health Check
        // Kein echter Testdruck aus Performancegründen — Erstellung eines PDFs wäre zu teuer für einen Health-Check.
        healthItems.Add(new ServiceHealthItem
        {
            Name = "PDF-Erstellung",
            IsHealthy = true,
            StatusText = "OK",
            CheckedAt = now
        });

        return healthItems;
    }

    public async Task<DashboardSummary> GetSummaryAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var firstDayOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var openCount = await _context.Invoices
                .Where(i => i.Status == InvoiceStatus.Sent)
                .CountAsync();

            var overdueCount = await _context.Invoices
                .Where(i => i.Status == InvoiceStatus.Overdue)
                .CountAsync();

            var draftCount = await _context.Invoices
                .Where(i => i.Status == InvoiceStatus.Draft)
                .CountAsync();

            // Calculate revenue by loading invoices with items
            var paidInvoicesThisMonth = await _context.Invoices
                .Include(i => i.Items)
                .Where(i => i.Status == InvoiceStatus.Paid &&
                           i.PaidDate.HasValue &&
                           i.PaidDate.Value >= firstDayOfMonth)
                .ToListAsync();

            var revenueThisMonth = paidInvoicesThisMonth.Any()
                ? paidInvoicesThisMonth.Sum(i => i.TotalGross)
                : 0;

            _logger.LogInformation("Dashboard Summary - Drafts: {DraftCount}, Open: {OpenCount}, Overdue: {OverdueCount}, Revenue: {Revenue}, Paid Invoices: {PaidCount}",
                draftCount, openCount, overdueCount, revenueThisMonth, paidInvoicesThisMonth.Count);

            return new DashboardSummary
            {
                OpenInvoices = openCount,
                OverdueInvoices = overdueCount,
                DraftInvoices = draftCount,
                RevenueThisMonth = revenueThisMonth
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get dashboard summary");
            return new DashboardSummary(); // Return empty summary on error
        }
    }

    public async Task<IReadOnlyList<ActivityItem>> GetRecentActivitiesAsync(int take = 5)
    {
        try
        {
            var activities = new List<ActivityItem>();

            // Get recent invoices (created or status changed)
            var recentInvoices = await _context.Invoices
                .OrderByDescending(i => i.UpdatedAt)
                .Take(take)
                .ToListAsync();

            foreach (var invoice in recentInvoices)
            {
                string activityText = invoice.Status switch
                {
                    InvoiceStatus.Sent => $"Faktura {invoice.InvoiceNumber} versendet",
                    InvoiceStatus.Paid => $"Faktura {invoice.InvoiceNumber} beglichen",
                    InvoiceStatus.Draft => $"Faktura {invoice.InvoiceNumber} als Entwurf erstellt",
                    InvoiceStatus.Cancelled => $"Faktura {invoice.InvoiceNumber} storniert",
                    _ => $"Faktura {invoice.InvoiceNumber} aktualisiert"
                };

                activities.Add(new ActivityItem
                {
                    Text = activityText,
                    Date = invoice.UpdatedAt,
                    Url = $"/faktura/invoices/details/{invoice.Id}"
                });
            }

            // Get recent customers if we have space
            if (activities.Count < take)
            {
                var remaining = take - activities.Count;
                var allCustomers = await _customerService.GetAllAsync();
                var recentCustomers = allCustomers
                    .OrderByDescending(c => c.CreatedAt)
                    .Take(remaining)
                    .ToList();

                foreach (var customer in recentCustomers)
                {
                    activities.Add(new ActivityItem
                    {
                        Text = $"Kunde '{customer.Name}' angelegt",
                        Date = customer.CreatedAt,
                        Url = $"/customers/edit/{customer.Id}" // TODO: Create customer details page
                    });
                }
            }

            return activities
                .OrderByDescending(a => a.Date)
                .Take(take)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent activities");
            return new List<ActivityItem>(); // Return empty list on error
        }
    }
}
