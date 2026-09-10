using Kuestencode.Werkbank.Recepta.Domain.Enums;

namespace Kuestencode.Werkbank.Recepta.Domain.Dtos;

/// <summary>
/// DTO zum Erstellen eines neuen Belegs.
/// </summary>
public class CreateDocumentDto
{
    public string DocumentNumber { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal AmountNet19 { get; set; }
    public decimal AmountTax19 { get; set; }
    public decimal AmountNet7 { get; set; }
    public decimal AmountTax7 { get; set; }
    public decimal AmountNet0 { get; set; }
    public decimal AmountNet { get; set; }
    public decimal AmountTax { get; set; }
    public decimal AmountGross { get; set; }
    public decimal? SkontoPercent { get; set; }
    public int? SkontoDays { get; set; }
    public decimal? SkontoAmountOverride { get; set; }
    public DocumentCategory Category { get; set; } = DocumentCategory.Other;
    public string? OcrRawText { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// DTO zum Aktualisieren eines Belegs.
/// </summary>
public class UpdateDocumentDto
{
    public Guid SupplierId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal AmountNet19 { get; set; }
    public decimal AmountTax19 { get; set; }
    public decimal AmountNet7 { get; set; }
    public decimal AmountTax7 { get; set; }
    public decimal AmountNet0 { get; set; }
    public decimal AmountNet { get; set; }
    public decimal AmountTax { get; set; }
    public decimal AmountGross { get; set; }
    public decimal? SkontoPercent { get; set; }
    public int? SkontoDays { get; set; }
    public decimal? SkontoAmountOverride { get; set; }
    public DocumentCategory Category { get; set; } = DocumentCategory.Other;
    public string? Notes { get; set; }
}

/// <summary>
/// DTO für eine Projekt-Zuteilung (Ausgabe).
/// </summary>
public class DocumentAllocationDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public decimal AllocatedNet { get; set; }
    public decimal AllocatedTax { get; set; }
    public decimal AllocatedGross { get; set; }
}

/// <summary>
/// DTO zum Setzen einer Projekt-Zuteilung (Eingabe).
/// Net und Tax werden aus Gross und dem TaxRate des Belegs rückgerechnet.
/// </summary>
public class SetAllocationDto
{
    public Guid ProjectId { get; set; }
    public decimal AllocatedGross { get; set; }
}

/// <summary>
/// Filter für die Belegabfrage.
/// </summary>
public class DocumentFilterDto
{
    public DocumentStatus? Status { get; set; }
    public DocumentCategory? Category { get; set; }
    public Guid? SupplierId { get; set; }
    public bool? HasBeenAttached { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public DateOnly? PaidFrom { get; set; }
    public DateOnly? PaidTo { get; set; }
    public string? Search { get; set; }
    public bool? IsOverdue { get; set; }
}

/// <summary>
/// Ergebnis eines Scan-Vorgangs (OCR + Pattern-Extraktion oder XRechnung/ZUGFeRD).
/// </summary>
public class ScanResultDto
{
    public string OcrRawText { get; set; } = string.Empty;
    public Dictionary<string, string> ExtractedFields { get; set; } = new();
    public Guid? SuggestedSupplierId { get; set; }
    public string? SuggestedSupplierName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal? AmountNet { get; set; }
    public decimal? AmountGross { get; set; }
    public decimal? AmountTax { get; set; }
    public decimal? TaxRate { get; set; }
    public string? Iban { get; set; }

    /// <summary>
    /// True wenn die Daten aus einer XRechnung/ZUGFeRD-Datei stammen (100% Trefferquote).
    /// </summary>
    public bool IsXRechnung { get; set; }

    /// <summary>
    /// Vollständige XRechnung-Daten inkl. Lieferanteninformationen für Pre-Fill.
    /// </summary>
    public XRechnungData? XRechnungData { get; set; }
}

/// <summary>
/// DTO für Dateianhang-Informationen.
/// </summary>
public class DocumentFileDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Eine Änderungsprotokoll-Zeile für die Anzeige im Frontend.
/// </summary>
public class AuditLogEntryDto
{
    public string Action { get; set; } = string.Empty;
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string ChangedByUserName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
}
