using InvoiceApp.Models.Dashboard;

namespace InvoiceApp.Services;

public interface IDashboardService
{
    Task<IReadOnlyList<ServiceHealthItem>> GetHealthAsync();
    Task<DashboardSummary> GetSummaryAsync();
    Task<IReadOnlyList<ActivityItem>> GetRecentActivitiesAsync(int take = 5);
}
