using InvoiceApp.Models;

namespace InvoiceApp.Services;

public class PreviewService : IPreviewService
{
    public Invoice GenerateSampleInvoice(Company company)
    {
        var sampleCustomer = new Customer
        {
            Id = 999,
            CustomerNumber = "K-2025-001",
            Name = "Musterfirma GmbH",
            Address = "Musterstraße 123",
            PostalCode = "12345",
            City = "Musterstadt",
            Email = "max@musterfirma.de"
        };

        var invoice = new Invoice
        {
            Id = 999,
            InvoiceNumber = "RE-2025-001",
            InvoiceDate = new DateTime(2025, 1, 17),
            DueDate = new DateTime(2025, 1, 31),
            Customer = sampleCustomer,
            CustomerId = sampleCustomer.Id,
            Items = new List<InvoiceItem>
            {
                new InvoiceItem
                {
                    Position = 1,
                    Description = "Webentwicklung - Frontend Implementierung",
                    Quantity = 8,
                    UnitPrice = 85.00m,
                    VatRate = company.IsKleinunternehmer ? 0 : 19
                },
                new InvoiceItem
                {
                    Position = 2,
                    Description = "Backend API-Integration",
                    Quantity = 5,
                    UnitPrice = 95.00m,
                    VatRate = company.IsKleinunternehmer ? 0 : 19
                },
                new InvoiceItem
                {
                    Position = 3,
                    Description = "Projektmanagement & Konzeption",
                    Quantity = 3,
                    UnitPrice = 75.00m,
                    VatRate = company.IsKleinunternehmer ? 0 : 19
                }
            }
        };

        // Totals are computed automatically via [NotMapped] properties

        return invoice;
    }
}
