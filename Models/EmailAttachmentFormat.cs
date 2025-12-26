namespace InvoiceApp.Models;

/// <summary>
/// Defines the format options for email attachments when sending invoices
/// </summary>
public enum EmailAttachmentFormat
{
    /// <summary>
    /// Standard PDF via QuestPDF
    /// </summary>
    NormalPdf,

    /// <summary>
    /// ZUGFeRD hybrid PDF with embedded XML (EN 16931 compliant)
    /// </summary>
    ZugferdPdf,

    /// <summary>
    /// XRechnung XML only (no PDF)
    /// </summary>
    XRechnungXmlOnly,

    /// <summary>
    /// XRechnung XML + separate normal PDF as two attachments
    /// </summary>
    XRechnungXmlAndPdf
}
