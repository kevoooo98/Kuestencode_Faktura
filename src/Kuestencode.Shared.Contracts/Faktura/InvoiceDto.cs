namespace Kuestencode.Shared.Contracts.Faktura;

public record InvoiceDto
{
    public int Id { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime InvoiceDate { get; init; }
    public DateTime? ServicePeriodStart { get; init; }
    public DateTime? ServicePeriodEnd { get; init; }
    public DateTime? DueDate { get; init; }
    public int CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public int? ProjectId { get; init; }
    public string? Notes { get; init; }
    public string Status { get; init; } = "Draft";
    public DateTime? PaidDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? EmailSentAt { get; init; }
    public string? EmailSentTo { get; init; }
    public int EmailSendCount { get; init; }
    public string? EmailCcRecipients { get; init; }
    public string? EmailBccRecipients { get; init; }
    public DateTime? PrintedAt { get; init; }
    public int PrintCount { get; init; }
    public string DiscountType { get; init; } = "None";
    public decimal? DiscountValue { get; init; }
    public bool IsReverseCharge { get; init; }
    public string Type { get; init; } = "Invoice";
    public int? RelatedInvoiceId { get; init; }
    public string? RelatedInvoiceNumber { get; init; }
    public decimal TotalNet { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal TotalNetAfterDiscount { get; init; }
    public decimal TotalVat { get; init; }
    public decimal TotalGross { get; init; }
    public decimal TotalDownPayments { get; init; }
    public decimal AmountDue { get; init; }
    public List<InvoiceItemDto> Items { get; init; } = [];
    public List<DownPaymentDto> DownPayments { get; init; } = [];
}

public record InvoiceItemDto
{
    public int Id { get; init; }
    public int InvoiceId { get; init; }
    public int Position { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal VatRate { get; init; }
    public decimal TotalNet { get; init; }
    public decimal TotalVat { get; init; }
    public decimal TotalGross { get; init; }
}

public record DownPaymentDto
{
    public int Id { get; init; }
    public int InvoiceId { get; init; }
    public string Description { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTime? PaymentDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? SourceInvoiceId { get; init; }
    public string? SourceInvoiceNumber { get; init; }
}

public record CreateInvoiceRequest
{
    public DateTime InvoiceDate { get; init; }
    public DateTime? ServicePeriodStart { get; init; }
    public DateTime? ServicePeriodEnd { get; init; }
    public DateTime? DueDate { get; init; }
    public int CustomerId { get; init; }
    public int? ProjectId { get; init; }
    public string? Notes { get; init; }
    public string DiscountType { get; init; } = "None";
    public decimal? DiscountValue { get; init; }
    public bool IsReverseCharge { get; init; }
    public string Type { get; init; } = "Invoice";
    public int? RelatedInvoiceId { get; init; }
    public List<CreateInvoiceItemRequest> Items { get; init; } = [];
}

public record CreateInvoiceItemRequest
{
    public string Description { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal VatRate { get; init; }
}

public record UpdateInvoiceRequest
{
    public DateTime InvoiceDate { get; init; }
    public DateTime? ServicePeriodStart { get; init; }
    public DateTime? ServicePeriodEnd { get; init; }
    public DateTime? DueDate { get; init; }
    public int CustomerId { get; init; }
    public int? ProjectId { get; init; }
    public string? Notes { get; init; }
    public string DiscountType { get; init; } = "None";
    public decimal? DiscountValue { get; init; }
    public bool IsReverseCharge { get; init; }
    public List<CreateInvoiceItemRequest> Items { get; init; } = [];
}

public record InvoiceFilterDto
{
    public string? Status { get; init; }
    public int? CustomerId { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public DateTime? PaidFrom { get; init; }
    public DateTime? PaidTo { get; init; }
    public string? SearchTerm { get; init; }
}
