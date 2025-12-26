namespace InvoiceApp.Models.Dashboard;

public class ServiceHealthItem
{
    public string Name { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public DateTime? CheckedAt { get; set; }
    public string? DetailMessage { get; set; }
}
