using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kuestencode.Faktura.Models;

public class DownPayment
{
    public int Id { get; set; }

    [Required]
    public int InvoiceId { get; set; }

    public Invoice Invoice { get; set; } = null!;

    [Required(ErrorMessage = "Beschreibung ist erforderlich")]
    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Betrag ist erforderlich")]
    [Range(0.01, 999999.99, ErrorMessage = "Betrag muss größer als 0 sein")]
    public decimal Amount { get; set; }

    public DateTime? PaymentDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? SourceInvoiceId { get; set; }

    // Abschlagsrechnung, auf die sich dieser Abschlag bezieht - wird separat geladen
    [NotMapped]
    public Invoice? SourceInvoice { get; set; }
}
