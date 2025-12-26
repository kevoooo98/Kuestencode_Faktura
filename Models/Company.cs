using System.ComponentModel.DataAnnotations;
using InvoiceApp.Validation;

namespace InvoiceApp.Models;

public class Company
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vollständiger Name ist erforderlich")]
    [MaxLength(200)]
    [FullName]
    public string OwnerFullName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? BusinessName { get; set; }

    [Required(ErrorMessage = "Adresse ist erforderlich")]
    [MaxLength(500)]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "PLZ ist erforderlich")]
    [GermanPostalCode]
    [MaxLength(10)]
    public string PostalCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Stadt ist erforderlich")]
    [MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Land ist erforderlich")]
    [MaxLength(100)]
    public string Country { get; set; } = "Deutschland";

    [Required(ErrorMessage = "Steuernummer ist erforderlich")]
    [MaxLength(50)]
    public string TaxNumber { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? VatId { get; set; }

    public bool IsKleinunternehmer { get; set; } = true;

    [Required(ErrorMessage = "Bankname ist erforderlich")]
    [MaxLength(100)]
    public string BankName { get; set; } = string.Empty;

    [Required(ErrorMessage = "IBAN ist erforderlich")]
    [Iban]
    [MaxLength(50)]
    public string BankAccount { get; set; } = string.Empty;

    [MaxLength(11)]
    public string? Bic { get; set; }

    [MaxLength(200)]
    public string? AccountHolder { get; set; }

    [Required(ErrorMessage = "Email ist erforderlich")]
    [EmailAddress(ErrorMessage = "Ungültige Email-Adresse")]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    [Url(ErrorMessage = "Ungültige URL")]
    public string? Website { get; set; }

    public int DefaultPaymentTermDays { get; set; } = 14;

    [MaxLength(10)]
    public string? InvoiceNumberPrefix { get; set; }

    [MaxLength(1000)]
    public string? FooterText { get; set; }

    // Logo stored as binary data in database
    public byte[]? LogoData { get; set; }

    [MaxLength(100)]
    public string? LogoContentType { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? EndpointId { get; set; }
    public string? EndpointSchemeId { get; set; }

    // SMTP Email Settings
    [MaxLength(200)]
    public string? SmtpHost { get; set; }

    public int? SmtpPort { get; set; }

    public bool SmtpUseSsl { get; set; } = true;

    [MaxLength(200)]
    public string? SmtpUsername { get; set; }

    [MaxLength(500)]
    public string? SmtpPassword { get; set; } // Encrypted

    [EmailAddress(ErrorMessage = "Ungültige Email-Adresse")]
    [MaxLength(200)]
    public string? EmailSenderEmail { get; set; }

    [MaxLength(200)]
    public string? EmailSenderName { get; set; }

    [MaxLength(2000)]
    public string? EmailSignature { get; set; }

    // Email Customization
    public EmailLayout EmailLayout { get; set; } = EmailLayout.Klar;

    [MaxLength(7)]
    public string EmailPrimaryColor { get; set; } = "#0F2A3D";

    [MaxLength(7)]
    public string EmailAccentColor { get; set; } = "#3FA796";

    [MaxLength(500)]
    public string? EmailGreeting { get; set; }

    [MaxLength(500)]
    public string? EmailClosing { get; set; }

    // PDF Customization
    public PdfLayout PdfLayout { get; set; } = PdfLayout.Klar;

    [MaxLength(7)]
    public string PdfPrimaryColor { get; set; } = "#1f3a5f";

    [MaxLength(7)]
    public string PdfAccentColor { get; set; } = "#3FA796";

    [MaxLength(500)]
    public string? PdfHeaderText { get; set; }

    [MaxLength(1000)]
    public string? PdfFooterText { get; set; }

    [MaxLength(500)]
    public string? PdfPaymentNotice { get; set; }
}
