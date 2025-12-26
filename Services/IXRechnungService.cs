namespace InvoiceApp.Services;

public interface IXRechnungService
{
    /// <summary>
    /// Generiert eine XRechnung als XML-String nach EN 16931 Standard
    /// </summary>
    Task<string> GenerateXRechnungXmlAsync(int invoiceId);

    /// <summary>
    /// Generiert ein ZUGFeRD-PDF (Hybrid: visuelles PDF + eingebettetes XML)
    /// </summary>
    Task<byte[]> GenerateZugferdPdfAsync(int invoiceId);

    /// <summary>
    /// Validiert ob alle Pflichtfelder für XRechnung vorhanden sind
    /// </summary>
    Task<(bool IsValid, List<string> MissingFields)> ValidateForXRechnungAsync(int invoiceId);
}
