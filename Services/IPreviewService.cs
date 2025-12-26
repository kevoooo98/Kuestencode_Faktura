using InvoiceApp.Models;

namespace InvoiceApp.Services;

public interface IPreviewService
{
    Invoice GenerateSampleInvoice(Company company);
}
