using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceApp.Models;

public class Invoice
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Rechnungsnummer ist erforderlich")]
    [MaxLength(20)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Rechnungsdatum ist erforderlich")]
    public DateTime InvoiceDate { get; set; }

    public DateTime? ServicePeriodStart { get; set; }

    public DateTime? ServicePeriodEnd { get; set; }

    public DateTime? DueDate { get; set; }

    [Required]
    public int CustomerId { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public DateTime? PaidDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Email Tracking
    public DateTime? EmailSentAt { get; set; }

    [MaxLength(200)]
    public string? EmailSentTo { get; set; }

    public int EmailSendCount { get; set; } = 0;

    // Print Tracking
    public DateTime? PrintedAt { get; set; }

    public int PrintCount { get; set; } = 0;

    // Discount
    public DiscountType DiscountType { get; set; } = DiscountType.None;
    public decimal? DiscountValue { get; set; }

    // Navigation Properties
    public Customer Customer { get; set; } = null!;
    public List<InvoiceItem> Items { get; set; } = new();
    public List<DownPayment> DownPayments { get; set; } = new();

    // Computed Properties
    [NotMapped]
    public decimal TotalNet => Items.Sum(i => i.TotalNet);

    [NotMapped]
    public decimal DiscountAmount
    {
        get
        {
            if (DiscountType == DiscountType.None || !DiscountValue.HasValue)
                return 0;

            if (DiscountType == DiscountType.Percentage)
                return TotalNet * (DiscountValue.Value / 100);

            return DiscountValue.Value; // Absolute
        }
    }

    [NotMapped]
    public decimal TotalNetAfterDiscount => TotalNet - DiscountAmount;

    [NotMapped]
    public decimal TotalVat
    {
        get
        {
            if (TotalNet == 0) return 0;

            // Calculate VAT proportionally after discount
            var discountRatio = TotalNetAfterDiscount / TotalNet;
            return Items.Sum(i => i.TotalVat) * discountRatio;
        }
    }

    [NotMapped]
    public decimal TotalGross => TotalNetAfterDiscount + TotalVat;

    [NotMapped]
    public decimal TotalDownPayments => DownPayments?.Sum(d => d.Amount) ?? 0;

    [NotMapped]
    public decimal AmountDue => TotalGross - TotalDownPayments;
}
