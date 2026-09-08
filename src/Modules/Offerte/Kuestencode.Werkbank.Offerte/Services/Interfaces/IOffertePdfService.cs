using Kuestencode.Core.Models;
using Kuestencode.Werkbank.Offerte.Domain.Entities;

namespace Kuestencode.Werkbank.Offerte.Services.Pdf;

/// <summary>
/// Service zur Erzeugung von Angebots-PDFs.
/// </summary>
public interface IOffertePdfService
{
    /// <summary>
    /// Erzeugt ein PDF für das angegebene Angebot mit expliziten Settings.
    /// Verwenden Sie diese Überladung, um Deadlocks in Blazor zu vermeiden.
    /// </summary>
    /// <param name="angebot">Das Angebot mit Positionen.</param>
    /// <param name="kunde">Die Kundendaten.</param>
    /// <param name="firma">Die Firmenstammdaten.</param>
    /// <param name="settings">Die PDF-Einstellungen.</param>
    /// <returns>PDF als Byte-Array.</returns>
    byte[] Erstelle(Angebot angebot, Customer kunde, Company firma, OfferteSettings settings);

    /// <summary>
    /// Erzeugt ein PDF für ein Angebot anhand der ID.
    /// Lädt Kunde und Firma automatisch.
    /// </summary>
    Task<byte[]> ErstelleAsync(Guid angebotId);
}
