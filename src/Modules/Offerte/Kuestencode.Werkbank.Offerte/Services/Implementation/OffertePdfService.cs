using Kuestencode.Core.Enums;
using Kuestencode.Core.Interfaces;
using Kuestencode.Core.Models;
using Kuestencode.Werkbank.Offerte.Data.Repositories;
using Kuestencode.Werkbank.Offerte.Domain.Entities;
using Kuestencode.Werkbank.Offerte.Services.Pdf.Layouts;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Kuestencode.Werkbank.Offerte.Services.Pdf;

/// <summary>
/// Service zur Erzeugung von Angebots-PDFs mit QuestPDF.
/// Verwendet Layout-Klassen für verschiedene Design-Stile.
/// </summary>
public class OffertePdfService : IOffertePdfService
{
    private readonly IAngebotRepository _repository;
    private readonly ICustomerService _customerService;
    private readonly ICompanyService _companyService;
    private readonly IOfferteSettingsService _settingsService;
    private readonly ILogger<OffertePdfService> _logger;

    public OffertePdfService(
        IAngebotRepository repository,
        ICustomerService customerService,
        ICompanyService companyService,
        IOfferteSettingsService settingsService,
        ILogger<OffertePdfService> logger)
    {
        _repository = repository;
        _customerService = customerService;
        _companyService = companyService;
        _settingsService = settingsService;
        _logger = logger;

        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]> ErstelleAsync(Guid angebotId)
    {
        var angebot = await _repository.GetByIdAsync(angebotId);
        if (angebot == null)
        {
            throw new InvalidOperationException($"Angebot mit ID {angebotId} nicht gefunden.");
        }

        var kunde = await _customerService.GetByIdAsync(angebot.KundeId);
        if (kunde == null)
        {
            throw new InvalidOperationException($"Kunde mit ID {angebot.KundeId} nicht gefunden.");
        }

        var firma = await _companyService.GetCompanyAsync();
        var settings = await _settingsService.GetSettingsAsync();

        return Erstelle(angebot, kunde, firma, settings);
    }

    public byte[] Erstelle(Angebot angebot, Customer kunde, Company firma, OfferteSettings settings)
    {
        _logger.LogInformation(
            "PDF-Generierung gestartet für Angebot {Angebotsnummer} mit Layout {Layout}",
            angebot.Angebotsnummer,
            settings.PdfLayout);

        var document = CreateLayoutDocument(angebot, kunde, firma, settings);
        var pdfBytes = document.GeneratePdf();

        _logger.LogInformation(
            "PDF-Generierung abgeschlossen für Angebot {Angebotsnummer}, Größe: {Size} Bytes",
            angebot.Angebotsnummer,
            pdfBytes.Length);

        return pdfBytes;
    }

    private IDocument CreateLayoutDocument(Angebot angebot, Customer kunde, Company firma, OfferteSettings settings)
    {
        return settings.PdfLayout switch
        {
            PdfLayout.Strukturiert => new OfferteStrukturiertLayout(angebot, kunde, firma, settings),
            PdfLayout.Betont => new OfferteBetontLayout(angebot, kunde, firma, settings),
            _ => new OfferteKlarLayout(angebot, kunde, firma, settings) // Klar ist der Default
        };
    }
}
