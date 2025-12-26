using InvoiceApp.Data;
using InvoiceApp.Models;
using Microsoft.EntityFrameworkCore;

namespace InvoiceApp.Services;

public class CompanyService : ICompanyService
{
    private readonly ApplicationDbContext _context;

    public CompanyService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Company> GetCompanyAsync()
    {
        var company = await _context.Companies.FirstOrDefaultAsync();

        if (company == null)
        {
            // Create default company if none exists
            company = new Company
            {
                OwnerFullName = "Max Mustermann",
                BusinessName = "IT-Solutions Mustermann",
                Address = "",
                PostalCode = "",
                City = "",
                Country = "Deutschland",
                TaxNumber = "",
                Email = "",
                BankName = "",
                BankAccount = "",
                IsKleinunternehmer = true,
                DefaultPaymentTermDays = 14,
                EmailLayout = EmailLayout.Klar,
                EmailPrimaryColor = "#0F2A3D",
                EmailAccentColor = "#3FA796",
                EmailGreeting = "Sehr geehrte Damen und Herren,\n\nanbei erhalten Sie Ihre Rechnung.",
                EmailClosing = "Mit freundlichen Grüßen\n\n{{Firmenname}}",
                PdfLayout = PdfLayout.Klar,
                PdfPrimaryColor = "#1f3a5f",
                PdfAccentColor = "#3FA796",
                PdfPaymentNotice = "Bitte überweisen Sie den Betrag bis zum {{Faelligkeitsdatum}} auf unser Konto."
            };

            _context.Companies.Add(company);
            await _context.SaveChangesAsync();
        }

        // Set AccountHolder to OwnerFullName if not set
        if (string.IsNullOrWhiteSpace(company.AccountHolder))
        {
            company.AccountHolder = company.OwnerFullName;
        }

        return company;
    }

    public async Task<Company> UpdateCompanyAsync(Company company)
    {
        var existing = await _context.Companies.FindAsync(company.Id);

        if (existing == null)
        {
            throw new InvalidOperationException("Firma nicht gefunden");
        }

        // Update all properties
        existing.OwnerFullName = company.OwnerFullName;
        existing.BusinessName = company.BusinessName;
        existing.Address = company.Address;
        existing.PostalCode = company.PostalCode;
        existing.City = company.City;
        existing.Country = company.Country;
        existing.TaxNumber = company.TaxNumber;
        existing.VatId = company.VatId;
        existing.IsKleinunternehmer = company.IsKleinunternehmer;
        existing.BankName = company.BankName;
        existing.BankAccount = company.BankAccount;
        existing.Bic = company.Bic;
        existing.AccountHolder = company.AccountHolder;
        existing.Email = company.Email;
        existing.Phone = company.Phone;
        existing.Website = company.Website;
        existing.DefaultPaymentTermDays = company.DefaultPaymentTermDays;
        existing.InvoiceNumberPrefix = company.InvoiceNumberPrefix;
        existing.FooterText = company.FooterText;
        existing.LogoData = company.LogoData;
        existing.LogoContentType = company.LogoContentType;

        // Update SMTP email settings
        existing.SmtpHost = company.SmtpHost;
        existing.SmtpPort = company.SmtpPort;
        existing.SmtpUseSsl = company.SmtpUseSsl;
        existing.SmtpUsername = company.SmtpUsername;
        existing.SmtpPassword = company.SmtpPassword;
        existing.EmailSenderEmail = company.EmailSenderEmail;
        existing.EmailSenderName = company.EmailSenderName;
        existing.EmailSignature = company.EmailSignature;

        // Update email customization
        existing.EmailLayout = company.EmailLayout;
        existing.EmailPrimaryColor = company.EmailPrimaryColor;
        existing.EmailAccentColor = company.EmailAccentColor;
        existing.EmailGreeting = company.EmailGreeting;
        existing.EmailClosing = company.EmailClosing;

        // Update PDF customization
        existing.PdfLayout = company.PdfLayout;
        existing.PdfPrimaryColor = company.PdfPrimaryColor;
        existing.PdfAccentColor = company.PdfAccentColor;
        existing.PdfHeaderText = company.PdfHeaderText;
        existing.PdfFooterText = company.PdfFooterText;
        existing.PdfPaymentNotice = company.PdfPaymentNotice;

        await _context.SaveChangesAsync();

        return existing;
    }

    public async Task<bool> HasCompanyDataAsync()
    {
        var company = await _context.Companies.FirstOrDefaultAsync();

        if (company == null)
        {
            return false;
        }

        // Check if essential fields are filled
        return !string.IsNullOrWhiteSpace(company.OwnerFullName) &&
               !string.IsNullOrWhiteSpace(company.Address) &&
               !string.IsNullOrWhiteSpace(company.PostalCode) &&
               !string.IsNullOrWhiteSpace(company.City) &&
               !string.IsNullOrWhiteSpace(company.TaxNumber) &&
               !string.IsNullOrWhiteSpace(company.Email) &&
               !string.IsNullOrWhiteSpace(company.BankName) &&
               !string.IsNullOrWhiteSpace(company.BankAccount);
    }

    public async Task<bool> IsEmailConfiguredAsync()
    {
        var company = await _context.Companies.FirstOrDefaultAsync();

        if (company == null)
        {
            return false;
        }

        // Check if SMTP email is configured
        return !string.IsNullOrWhiteSpace(company.EmailSenderEmail) &&
               !string.IsNullOrWhiteSpace(company.SmtpHost) &&
               company.SmtpPort.HasValue &&
               !string.IsNullOrWhiteSpace(company.SmtpUsername) &&
               !string.IsNullOrWhiteSpace(company.SmtpPassword);
    }
}
