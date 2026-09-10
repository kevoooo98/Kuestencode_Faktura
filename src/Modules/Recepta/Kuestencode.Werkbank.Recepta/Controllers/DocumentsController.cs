using Kuestencode.Werkbank.Recepta.Controllers.Dtos;
using Kuestencode.Werkbank.Recepta.Data.Repositories;
using Kuestencode.Werkbank.Recepta.Domain.Dtos;
using Kuestencode.Werkbank.Recepta.Domain.Enums;
using Kuestencode.Werkbank.Recepta.Services;
using Kuestencode.Werkbank.Recepta.Services.Interfaces;
using Kuestencode.Shared.Contracts.Recepta;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.UI.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Kuestencode.Werkbank.Recepta.Controllers;

[ApiController]
[Route("api/recepta/documents")]
[RequireRole(UserRole.Admin, UserRole.Buero)]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IDocumentPaymentService _paymentService;
    private readonly IDocumentAllocationRepository _allocationRepository;
    private readonly IOcrService _ocrService;
    private readonly IOcrPatternService _patternService;
    private readonly ILogger<DocumentsController> _logger;

    private static readonly string[] AllowedMimeTypes =
        ["application/pdf", "image/jpeg", "image/png", "image/tiff"];

    public DocumentsController(
        IDocumentService documentService,
        IDocumentPaymentService paymentService,
        IDocumentAllocationRepository allocationRepository,
        IOcrService ocrService,
        IOcrPatternService patternService,
        ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _paymentService = paymentService;
        _allocationRepository = allocationRepository;
        _ocrService = ocrService;
        _patternService = patternService;
        _logger = logger;
    }

    private ActionResult? ValidateUpload(IFormFile file)
    {
        if (file.Length > 52_428_800)
            return BadRequest(new { error = "Datei darf maximal 50 MB groß sein." });
        if (!AllowedMimeTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = "Nur PDF, JPEG, PNG und TIFF sind erlaubt." });
        return null;
    }

    /// <summary>
    /// Lädt alle Belege mit optionalem Filter.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentDto>>> GetAll(
        [FromQuery] string? status = null,
        [FromQuery] string? category = null,
        [FromQuery] Guid? supplierId = null,
        [FromQuery] bool? hasBeenAttached = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] DateOnly? paidFrom = null,
        [FromQuery] DateOnly? paidTo = null,
        [FromQuery] string? search = null)
    {
        var filter = new DocumentFilterDto
        {
            SupplierId = supplierId,
            HasBeenAttached = hasBeenAttached,
            From = from,
            To = to,
            PaidFrom = paidFrom,
            PaidTo = paidTo,
            Search = search
        };

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<DocumentStatus>(status, true, out var parsedStatus))
        {
            filter.Status = parsedStatus;
        }

        if (!string.IsNullOrEmpty(category) && Enum.TryParse<DocumentCategory>(category, true, out var parsedCategory))
        {
            filter.Category = parsedCategory;
        }

        var documents = await _documentService.GetAllAsync(filter);
        return Ok(documents);
    }

    /// <summary>
    /// Lädt einen Beleg anhand der ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> GetById(Guid id)
    {
        var document = await _documentService.GetByIdAsync(id);
        if (document == null)
        {
            return NotFound();
        }

        return Ok(document);
    }

    /// <summary>
    /// Lädt das Änderungsprotokoll (GoBD-Audit-Log) eines Belegs.
    /// </summary>
    [HttpGet("{id:guid}/audit-log")]
    public async Task<ActionResult<List<AuditLogEntryDto>>> GetAuditLog(Guid id)
    {
        var entries = await _documentService.GetAuditLogAsync(id);
        return Ok(entries);
    }

    /// <summary>
    /// Rechnet die Audit-Log-Hashkette von Genesis bis zum letzten Eintrag durch und meldet den
    /// ersten Bruch. Das ist der eigentliche Nachweis der Unveränderbarkeit (GoBD) — ohne diesen
    /// Endpunkt wären Hash/PreviousHash nur gespeicherte Daten ohne Beweiskraft.
    /// </summary>
    [HttpGet("audit-log/verify")]
    [RequireRole(UserRole.Admin)]
    public async Task<ActionResult<AuditChainVerificationDto>> VerifyAuditLogChain()
    {
        var result = await _documentService.VerifyAuditLogChainAsync();
        return Ok(new AuditChainVerificationDto(result.IsValid, result.BrokenAtSequenceNumber, result.Reason));
    }

    /// <summary>
    /// Erstellt einen neuen Beleg.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<DocumentDto>> Create([FromBody] CreateDocumentRequest request)
    {
        try
        {
            if (!Enum.TryParse<DocumentCategory>(request.Category, true, out var category))
            {
                category = DocumentCategory.Other;
            }

            var dto = new CreateDocumentDto
            {
                DocumentNumber = request.DocumentNumber,
                SupplierId = request.SupplierId,
                InvoiceNumber = request.InvoiceNumber,
                InvoiceDate = request.InvoiceDate,
                DueDate = request.DueDate,
                AmountNet19 = request.AmountNet19,
                AmountTax19 = request.AmountTax19,
                AmountNet7 = request.AmountNet7,
                AmountTax7 = request.AmountTax7,
                AmountNet0 = request.AmountNet0,
                AmountNet = request.AmountNet,
                AmountTax = request.AmountTax,
                AmountGross = request.AmountGross,
                Category = category,
                OcrRawText = request.OcrRawText,
                Notes = request.Notes
            };

            var document = await _documentService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = document.Id }, document);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Erstellt einen Beleg-Vorschlag aus einem Scan (OCR + Pattern-Extraktion).
    /// </summary>
    [HttpPost("scan")]
    public async Task<ActionResult<ScanResultDto>> CreateFromScan(IFormFile file)
    {
        var validationError = ValidateUpload(file);
        if (validationError != null) return validationError;

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _documentService.CreateFromScanAsync(stream, file.FileName);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Scan von '{FileName}'", file.FileName);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Aktualisiert einen Beleg.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> Update(Guid id, [FromBody] UpdateDocumentRequest request)
    {
        try
        {
            if (!Enum.TryParse<DocumentCategory>(request.Category, true, out var category))
            {
                category = DocumentCategory.Other;
            }

            var dto = new UpdateDocumentDto
            {
                SupplierId = request.SupplierId,
                InvoiceNumber = request.InvoiceNumber,
                InvoiceDate = request.InvoiceDate,
                DueDate = request.DueDate,
                AmountNet19 = request.AmountNet19,
                AmountTax19 = request.AmountTax19,
                AmountNet7 = request.AmountNet7,
                AmountTax7 = request.AmountTax7,
                AmountNet0 = request.AmountNet0,
                AmountNet = request.AmountNet,
                AmountTax = request.AmountTax,
                AmountGross = request.AmountGross,
                Category = category,
                Notes = request.Notes
            };

            var document = await _documentService.UpdateAsync(id, dto);
            return Ok(document);
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Ändert den Status eines Belegs.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeDocumentStatusRequest request)
    {
        try
        {
            if (!Enum.TryParse<DocumentStatus>(request.NewStatus, true, out var newStatus))
            {
                return BadRequest(new { error = $"Ungültiger Status: {request.NewStatus}" });
            }

            await _documentService.ChangeStatusAsync(id, newStatus, request.PaidDate);
            return Ok(new { message = $"Status geändert auf '{newStatus}'." });
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Triggert den Lerneffekt für einen Beleg.
    /// </summary>
    [HttpPost("{id:guid}/learn")]
    public async Task<IActionResult> LearnPatterns(Guid id)
    {
        try
        {
            await _documentService.LearnPatternsAsync(id);
            return Ok(new { message = "Patterns gelernt." });
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Löscht einen Beleg (nur bei Status Draft).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _documentService.DeleteAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Generiert die nächste verfügbare Belegnummer.
    /// </summary>
    [HttpGet("next-number")]
    public async Task<ActionResult<string>> GetNextNumber()
    {
        var number = await _documentService.GenerateDocumentNumberAsync();
        return Ok(new { number });
    }

    /// <summary>
    /// Führt OCR auf einer hochgeladenen Datei aus (ohne sie an einen Beleg zu binden).
    /// </summary>
    [HttpPost("ocr")]
    public async Task<ActionResult> ExtractText(IFormFile file)
    {
        var validationError = ValidateUpload(file);
        if (validationError != null) return validationError;

        try
        {
            await using var stream = file.OpenReadStream();
            var text = await _ocrService.ExtractTextAsync(stream, file.FileName);
            return Ok(new { text, fileName = file.FileName, charCount = text.Length });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR-Fehler bei Datei '{FileName}'", file.FileName);
            return BadRequest(new { error = $"OCR-Fehler: {ex.Message}" });
        }
    }

    /// <summary>
    /// Lernt ein OCR-Pattern aus einem Benutzerwert.
    /// </summary>
    [HttpPost("ocr/learn")]
    public async Task<IActionResult> LearnPattern([FromBody] LearnPatternRequest request)
    {
        try
        {
            await _patternService.LearnPatternAsync(
                request.SupplierId, request.FieldName, request.OcrText, request.UserValue);
            return Ok(new { message = "Pattern gelernt." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Lernen des Patterns für Feld '{FieldName}'", request.FieldName);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Extrahiert Felder aus einem OCR-Text anhand gelernter und generischer Patterns.
    /// </summary>
    [HttpPost("ocr/extract")]
    public async Task<ActionResult<Dictionary<string, string>>> ExtractFields([FromBody] ExtractFieldsRequest request)
    {
        try
        {
            var fields = await _patternService.ExtractFieldsAsync(request.SupplierId, request.OcrText);
            return Ok(fields);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler bei der Feld-Extraktion");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Liefert alle Zahlungen im Zeitraum, angereichert mit Belegdaten, für die EÜR.
    /// Jede Teilzahlung erscheint als eigener Eintrag.
    /// </summary>
    [HttpGet("euer-payments")]
    public async Task<ActionResult<List<ReceptaPaymentDto>>> GetEuerPayments(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        var payments = await _paymentService.GetEuerPaymentsAsync(from, to);
        return Ok(payments);
    }

    /// <summary>
    /// Lädt alle Belege eines Projekts via Allokationen (für Cross-Modul-Integration mit Acta).
    /// </summary>
    [HttpGet("project/{projectId:guid}")]
    public async Task<ActionResult<List<ReceptaDocumentDto>>> GetByProject(Guid projectId, [FromQuery] bool onlyUnattached = false)
    {
        var allocations = await _allocationRepository.GetByProjectIdAsync(projectId);

        var result = allocations
            .Where(x => !onlyUnattached || !x.Document.HasBeenAttached)
            .Select(x => new ReceptaDocumentDto
            {
                Id = x.Document.Id,
                DocumentNumber = x.Document.DocumentNumber,
                SupplierName = x.Document.Supplier?.Name ?? string.Empty,
                InvoiceNumber = x.Document.InvoiceNumber,
                InvoiceDate = x.Document.InvoiceDate,
                PaidDate = x.Document.PaidDate,
                AmountNet = x.Document.AmountNet,
                AmountTax = x.Document.AmountTax,
                AmountGross = x.Document.AmountGross,
                TaxRate = x.Document.TaxRate,
                Category = x.Document.Category.ToString(),
                Status = x.Document.Status.ToString(),
                HasBeenAttached = x.Document.HasBeenAttached,
                SkontoApplied = x.Document.SkontoApplied,
                SkontoAmount = x.Document.SkontoAmount,
                AllocatedNet = x.Allocation.AllocatedNet,
                AllocatedGross = x.Allocation.AllocatedGross
            }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Liefert die Kostenübersicht eines Projekts (Aufwand extern für Acta).
    /// Beträge basieren auf den projektseitigen Allokationen (nicht Gesamtbeträge).
    /// </summary>
    [HttpGet("project/{projectId:guid}/expenses")]
    public async Task<ActionResult<ProjectExpensesResponseDto>> GetProjectExpenses(Guid projectId)
    {
        var allocations = await _allocationRepository.GetByProjectIdAsync(projectId);

        var response = new ProjectExpensesResponseDto
        {
            ProjectId = projectId,
            TotalNet = allocations.Sum(x => x.Allocation.AllocatedNet),
            TotalGross = allocations.Sum(x => x.Allocation.AllocatedGross),
            DocumentCount = allocations.Count,
            Documents = allocations.Select(x => new ReceptaDocumentDto
            {
                Id = x.Document.Id,
                DocumentNumber = x.Document.DocumentNumber,
                SupplierName = x.Document.Supplier?.Name ?? string.Empty,
                InvoiceNumber = x.Document.InvoiceNumber,
                InvoiceDate = x.Document.InvoiceDate,
                PaidDate = x.Document.PaidDate,
                AmountNet = x.Document.AmountNet,
                AmountTax = x.Document.AmountTax,
                AmountGross = x.Document.AmountGross,
                TaxRate = x.Document.TaxRate,
                Category = x.Document.Category.ToString(),
                Status = x.Document.Status.ToString(),
                HasBeenAttached = x.Document.HasBeenAttached,
                SkontoApplied = x.Document.SkontoApplied,
                SkontoAmount = x.Document.SkontoAmount,
                AllocatedNet = x.Allocation.AllocatedNet,
                AllocatedGross = x.Allocation.AllocatedGross
            }).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Lädt die Projekt-Zuteilungen eines Belegs.
    /// </summary>
    [HttpGet("{id:guid}/allocations")]
    public async Task<ActionResult<List<DocumentAllocationDto>>> GetAllocations(Guid id)
    {
        try
        {
            var allocations = await _documentService.GetAllocationsAsync(id);
            return Ok(allocations);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Setzt die Projekt-Zuteilungen eines Belegs.
    /// Die Summe der Brutto-Zuteilungen darf den Gesamtbetrag nicht überschreiten.
    /// Nur bei Status Draft erlaubt.
    /// </summary>
    [HttpPut("{id:guid}/allocations")]
    public async Task<ActionResult<List<DocumentAllocationDto>>> SetAllocations(
        Guid id, [FromBody] List<SetAllocationRequest> request)
    {
        try
        {
            var dtos = request.Select(r => new SetAllocationDto
            {
                ProjectId = r.ProjectId,
                AllocatedGross = r.AllocatedGross
            });

            await _documentService.SetAllocationsAsync(id, dtos);
            var updated = await _documentService.GetAllocationsAsync(id);
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("nicht gefunden"))
            {
                return NotFound(new { error = ex.Message });
            }
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    [HttpPost("mark-attached")]
    public async Task<IActionResult> MarkAttached([FromBody] MarkDocumentsAttachedRequest request)
    {
        if (request.DocumentIds.Count == 0)
        {
            return Ok(new { updated = 0 });
        }

        await _documentService.MarkAsAttachedAsync(request.DocumentIds);
        return Ok(new { updated = request.DocumentIds.Distinct().Count() });
    }

    [HttpPatch("{id:guid}/skonto-applied")]
    public async Task<IActionResult> SetSkontoApplied(Guid id, [FromBody] bool applied)
    {
        try
        {
            await _documentService.SetSkontoAppliedAsync(id, applied);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}

public record AuditChainVerificationDto(bool IsValid, long? BrokenAtSequenceNumber, string? Reason);
