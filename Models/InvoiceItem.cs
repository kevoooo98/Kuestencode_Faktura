using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceApp.Models;

public class InvoiceItem
{
    public int Id { get; set; }

    [Required]
    public int InvoiceId { get; set; }

    public int Position { get; set; }

    [Required(ErrorMessage = "Beschreibung ist erforderlich")]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Menge ist erforderlich")]
    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }

    [Required(ErrorMessage = "Einzelpreis ist erforderlich")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal VatRate { get; set; } = 0;

    // Navigation Properties
    public Invoice Invoice { get; set; } = null!;

    // Computed Properties
    [NotMapped]
    public decimal TotalNet => Math.Round(Quantity * UnitPrice, 2);

    [NotMapped]
    public decimal TotalVat => Math.Round(TotalNet * VatRate / 100, 2);

    [NotMapped]
    public decimal TotalGross => TotalNet + TotalVat;
}
