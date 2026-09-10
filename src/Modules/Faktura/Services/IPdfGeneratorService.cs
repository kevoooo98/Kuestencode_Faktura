using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;

namespace Kuestencode.Faktura.Services;

public interface IPdfGeneratorService
{
    Task<byte[]> GenerateInvoicePdfAsync(int invoiceId);
    byte[] GenerateInvoicePdf(int invoiceId);
    byte[] GeneratePdfWithCompany(Invoice invoice, Company company);
    /// <summary>
    /// Rendert das aktuelle PDF und speichert es unveränderlich als Anhang (GoBD), sofern für
    /// diese Rechnung noch kein eingefrorenes PDF existiert (idempotent).
    /// </summary>
    Task FreezeSnapshotAsync(int invoiceId);

    /// <summary>
    /// Liefert das eingefrorene PDF zurück, falls eines existiert, sonst null.
    /// </summary>
    Task<byte[]?> TryGetFrozenSnapshotAsync(int invoiceId);
}
