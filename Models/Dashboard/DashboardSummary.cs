namespace InvoiceApp.Models.Dashboard;

public class DashboardSummary
{
    public int OpenInvoices { get; set; }
    public int OverdueInvoices { get; set; }
    public int DraftInvoices { get; set; }
    public decimal RevenueThisMonth { get; set; }
}
