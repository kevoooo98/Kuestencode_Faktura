using InvoiceApp.Models;

namespace InvoiceApp.Services;

public interface IPdfGeneratorService
{
    byte[] GenerateInvoicePdf(int invoiceId);
    byte[] GeneratePdfWithCompany(Invoice invoice, Company company);
    Task<string> GenerateAndSaveAsync(int invoiceId);
}
