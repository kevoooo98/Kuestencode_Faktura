using Microsoft.AspNetCore.Mvc;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services;
using Kuestencode.Faktura.Services.Pdf;
using Kuestencode.Shared.Contracts.Faktura;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.UI.Auth;

namespace Kuestencode.Faktura.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequireRole(UserRole.Admin, UserRole.Buero)]
public class InvoiceController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;
    private readonly IInvoicePaymentService _paymentService;
    private readonly IPdfGeneratorService _pdfGeneratorService;
    private readonly IPdfMergeService _pdfMergeService;
    private readonly IEmailService _emailService;
    private readonly IHostApiClient _hostApiClient;
    private readonly ILogger<InvoiceController> _logger;

    public InvoiceController(
        IInvoiceService invoiceService,
        IInvoicePaymentService paymentService,
        IPdfGeneratorService pdfGeneratorService,
        IPdfMergeService pdfMergeService,
        IEmailService emailService,
        IHostApiClient hostApiClient,
        ILogger<InvoiceController> logger)
    {
        _invoiceService = invoiceService;
        _paymentService = paymentService;
        _pdfGeneratorService = pdfGeneratorService;
        _pdfMergeService = pdfMergeService;
        _emailService = emailService;
        _hostApiClient = hostApiClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<InvoiceDto>>> GetAll(
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] int? customerId = null,
        [FromQuery] DateTime? paidFrom = null,
        [FromQuery] DateTime? paidTo = null)
    {
        try
        {
            List<Invoice> invoices;

            // Zufluss-/Abflussprinzip: direkt via DB nach PaidDate filtern (performant)
            if (paidFrom.HasValue && paidTo.HasValue)
            {
                invoices = await _invoiceService.GetPaidByDateRangeAsync(paidFrom.Value, paidTo.Value);
            }
            else if (!string.IsNullOrEmpty(status) && Enum.TryParse<InvoiceStatus>(status, out var invoiceStatus))
            {
                invoices = await _invoiceService.GetByStatusAsync(invoiceStatus);

                if (paidFrom.HasValue)
                    invoices = invoices.Where(i => i.PaidDate.HasValue && i.PaidDate.Value >= paidFrom.Value).ToList();
                if (paidTo.HasValue)
                    invoices = invoices.Where(i => i.PaidDate.HasValue && i.PaidDate.Value <= paidTo.Value).ToList();
            }
            else if (!string.IsNullOrEmpty(type) && Enum.TryParse<InvoiceType>(type, out var invoiceType))
            {
                invoices = await _invoiceService.GetByTypeAsync(invoiceType);
            }
            else
            {
                invoices = await _invoiceService.GetAllAsync();
            }

            // Filter by customer if specified
            if (customerId.HasValue)
            {
                invoices = invoices.Where(i => i.CustomerId == customerId.Value).ToList();
            }

            var dtos = new List<InvoiceDto>();
            foreach (var invoice in invoices)
            {
                var dto = await MapToDtoAsync(invoice);
                dtos.Add(dto);
            }

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving invoices");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<InvoiceDto>> GetById(int id)
    {
        try
        {
            var invoice = await _invoiceService.GetByIdAsync(id);
            if (invoice == null) return NotFound();

            var dto = await MapToDtoAsync(invoice);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving invoice {InvoiceId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{id}/pdf-print")]
    public async Task<IActionResult> GetPdfForPrint(int id)
    {
        try
        {
            var invoice = await _invoiceService.GetByIdAsync(id);
            if (invoice == null) return NotFound();

            var pdfBytes = await _pdfGeneratorService.TryGetFrozenSnapshotAsync(id)
                ?? await _pdfGeneratorService.GenerateInvoicePdfAsync(id);
            var mergedBytes = _pdfMergeService.MergeForPrint(pdfBytes, invoice.Attachments);
            var base64 = Convert.ToBase64String(mergedBytes);

            var html = $$"""
                <!DOCTYPE html>
                <html>
                <head>
                  <title>{{invoice.InvoiceNumber}}</title>
                  <style>html,body,embed{margin:0;padding:0;width:100%;height:100%;}</style>
                </head>
                <body>
                  <embed src="data:application/pdf;base64,{{base64}}" type="application/pdf" width="100%" height="100%">
                  <script>
                    setTimeout(function() { window.print(); }, 1500);
                  </script>
                </body>
                </html>
                """;

            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating print PDF for invoice {InvoiceId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost]
    public async Task<ActionResult<InvoiceDto>> Create([FromBody] CreateInvoiceRequest request)
    {
        try
        {
            var invoice = new Invoice
            {
                InvoiceDate = DateTime.SpecifyKind(request.InvoiceDate, DateTimeKind.Utc),
                ServicePeriodStart = request.ServicePeriodStart.HasValue ? DateTime.SpecifyKind(request.ServicePeriodStart.Value, DateTimeKind.Utc) : null,
                ServicePeriodEnd = request.ServicePeriodEnd.HasValue ? DateTime.SpecifyKind(request.ServicePeriodEnd.Value, DateTimeKind.Utc) : null,
                DueDate = request.DueDate.HasValue ? DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc) : null,
                CustomerId = request.CustomerId,
                ProjectId = request.ProjectId,
                Notes = request.Notes,
                DiscountType = Enum.Parse<DiscountType>(request.DiscountType),
                DiscountValue = request.DiscountValue,
                Type = Enum.Parse<InvoiceType>(request.Type),
                RelatedInvoiceId = request.RelatedInvoiceId,
                Status = InvoiceStatus.Draft,
                Items = request.Items.Select(item => new InvoiceItem
                {
                    Description = item.Description,
                    Unit = item.Unit,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    VatRate = item.VatRate
                }).ToList()
            };

            var created = await _invoiceService.CreateAsync(invoice);
            var dto = await MapToDtoAsync(created);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating invoice");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateInvoiceRequest request)
    {
        try
        {
            var invoice = await _invoiceService.GetByIdAsync(id);
            if (invoice == null) return NotFound();

            invoice.InvoiceDate = DateTime.SpecifyKind(request.InvoiceDate, DateTimeKind.Utc);
            invoice.ServicePeriodStart = request.ServicePeriodStart.HasValue ? DateTime.SpecifyKind(request.ServicePeriodStart.Value, DateTimeKind.Utc) : null;
            invoice.ServicePeriodEnd = request.ServicePeriodEnd.HasValue ? DateTime.SpecifyKind(request.ServicePeriodEnd.Value, DateTimeKind.Utc) : null;
            invoice.DueDate = request.DueDate.HasValue ? DateTime.SpecifyKind(request.DueDate.Value, DateTimeKind.Utc) : null;
            invoice.CustomerId = request.CustomerId;
            invoice.ProjectId = request.ProjectId;
            invoice.Notes = request.Notes;
            invoice.DiscountType = Enum.Parse<DiscountType>(request.DiscountType);
            invoice.DiscountValue = request.DiscountValue;

            // Update items
            invoice.Items.Clear();
            invoice.Items = request.Items.Select(item => new InvoiceItem
            {
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                VatRate = item.VatRate
            }).ToList();

            await _invoiceService.UpdateAsync(invoice);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating invoice {InvoiceId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _invoiceService.DeleteAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting invoice {InvoiceId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{id}/audit-log")]
    public async Task<ActionResult<List<AuditLogEntryDto>>> GetAuditLog(int id)
    {
        try
        {
            var entries = await _invoiceService.GetAuditLogAsync(id);
            return Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit log for invoice {InvoiceId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelInvoiceRequest? request = null)
    {
        try
        {
            await _invoiceService.CancelAsync(id, request?.Reason);
            return Ok(new { message = "Invoice cancelled" });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling invoice {InvoiceId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("{id}/send")]
    public async Task<IActionResult> SendInvoice(int id, [FromBody] SendInvoiceRequest? request = null)
    {
        try
        {
            var invoice = await _invoiceService.GetByIdAsync(id);
            if (invoice == null) return NotFound();

            // Use customer email if not provided
            string recipientEmail = request?.RecipientEmail ?? invoice.Customer?.Email ?? string.Empty;
            if (string.IsNullOrEmpty(recipientEmail))
            {
                return BadRequest("Recipient email is required");
            }

            await _emailService.SendInvoiceEmailAsync(
                id,
                recipientEmail,
                request?.CustomMessage,
                request?.Format ?? EmailAttachmentFormat.NormalPdf,
                request?.CcEmails,
                request?.BccEmails);

            return Ok(new { message = "Invoice email sent successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending invoice {InvoiceId}", id);
            return StatusCode(500, ex.Message);
        }
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GeneratePdf(int id)
    {
        try
        {
            var invoice = await _invoiceService.GetByIdAsync(id);
            var pdfBytes = await _pdfGeneratorService.TryGetFrozenSnapshotAsync(id)
                ?? await _pdfGeneratorService.GenerateInvoicePdfAsync(id);
            return File(pdfBytes, "application/pdf", $"Invoice-{invoice?.InvoiceNumber ?? id.ToString()}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF for invoice {InvoiceId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("{id}/mark-paid")]
    public async Task<IActionResult> MarkAsPaid(int id, [FromBody] MarkAsPaidRequest request)
    {
        try
        {
            await _invoiceService.MarkAsPaidAsync(id, request.PaidDate);
            return Ok(new { message = "Invoice marked as paid" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking invoice {InvoiceId} as paid", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Liefert alle Zahlungen im Zeitraum, angereichert mit Rechnungspositionen, für die EÜR.
    /// Jede Teilzahlung erscheint als eigener Eintrag.
    /// </summary>
    [HttpGet("euer-payments")]
    public async Task<ActionResult<List<InvoiceEuerPaymentDto>>> GetEuerPayments(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        try
        {
            var payments = await _paymentService.GetByPaymentDateRangeAsync(
                DateTime.SpecifyKind(from, DateTimeKind.Utc),
                DateTime.SpecifyKind(to, DateTimeKind.Utc));

            // Resolve customer names once per unique CustomerId
            var customerIds = payments.Select(p => p.Invoice.CustomerId).Distinct();
            var customerNames = new Dictionary<int, string?>();
            foreach (var customerId in customerIds)
            {
                var customer = await _hostApiClient.GetCustomerAsync(customerId);
                customerNames[customerId] = customer?.Name;
            }

            var result = payments.Select(p => new InvoiceEuerPaymentDto
            {
                PaymentId = p.Id,
                InvoiceId = p.InvoiceId,
                InvoiceNumber = p.Invoice.InvoiceNumber,
                InvoiceType = p.Invoice.Type.ToString(),
                InvoiceDate = p.Invoice.InvoiceDate,
                PaymentDate = DateOnly.FromDateTime(p.PaymentDate),
                PaymentAmount = p.Amount,
                InvoiceTotalGross = p.Invoice.TotalGross,
                CustomerName = customerNames.GetValueOrDefault(p.Invoice.CustomerId),
                Items = p.Invoice.Items.Select(item => new InvoiceItemDto
                {
                    Id = item.Id,
                    InvoiceId = item.InvoiceId,
                    Position = item.Position,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    VatRate = item.VatRate,
                    TotalNet = item.TotalNet,
                    TotalVat = item.TotalVat,
                    TotalGross = item.TotalGross
                }).ToList()
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Laden der EÜR-Zahlungen für {From} - {To}", from, to);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<ActionResult<ProjectInvoicesResponseDto>> GetByProjectId(int projectId)
    {
        try
        {
            var invoices = await _invoiceService.GetByProjectIdAsync(projectId);
            var dtos = new List<InvoiceDto>();
            foreach (var invoice in invoices)
            {
                dtos.Add(await MapToDtoAsync(invoice));
            }

            var result = new ProjectInvoicesResponseDto
            {
                ProjectId = projectId,
                TotalNet = dtos.Sum(i => i.TotalNetAfterDiscount),
                TotalGross = dtos.Sum(i => i.TotalGross),
                InvoiceCount = dtos.Count,
                Invoices = dtos
            };
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving invoices for project {ProjectId}", projectId);
            return StatusCode(500, "Internal server error");
        }
    }

    private async Task<InvoiceDto> MapToDtoAsync(Invoice invoice)
    {
        // Load customer name from Host API
        string? customerName = null;
        if (invoice.CustomerId > 0)
        {
            var customer = await _hostApiClient.GetCustomerAsync(invoice.CustomerId);
            customerName = customer?.Name;
        }

        string? relatedInvoiceNumber = null;
        if (invoice.RelatedInvoiceId.HasValue)
        {
            var relatedInvoice = await _invoiceService.GetByIdAsync(invoice.RelatedInvoiceId.Value, includeCustomer: false, includeItems: false);
            relatedInvoiceNumber = relatedInvoice?.InvoiceNumber;
        }

        var sourceInvoiceNumbers = new Dictionary<int, string>();
        foreach (var sourceInvoiceId in invoice.DownPayments.Where(d => d.SourceInvoiceId.HasValue).Select(d => d.SourceInvoiceId!.Value).Distinct())
        {
            var sourceInvoice = await _invoiceService.GetByIdAsync(sourceInvoiceId, includeCustomer: false, includeItems: false);
            if (sourceInvoice != null)
            {
                sourceInvoiceNumbers[sourceInvoiceId] = sourceInvoice.InvoiceNumber;
            }
        }

        return new InvoiceDto
        {
            Type = invoice.Type.ToString(),
            RelatedInvoiceId = invoice.RelatedInvoiceId,
            RelatedInvoiceNumber = relatedInvoiceNumber,
            Id = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceDate = invoice.InvoiceDate,
            ServicePeriodStart = invoice.ServicePeriodStart,
            ServicePeriodEnd = invoice.ServicePeriodEnd,
            DueDate = invoice.DueDate,
            CustomerId = invoice.CustomerId,
            CustomerName = customerName,
            ProjectId = invoice.ProjectId,
            Notes = invoice.Notes,
            Status = invoice.Status.ToString(),
            PaidDate = invoice.PaidDate,
            CancelledAt = invoice.CancelledAt,
            CancellationReason = invoice.CancellationReason,
            CreatedAt = invoice.CreatedAt,
            UpdatedAt = invoice.UpdatedAt,
            EmailSentAt = invoice.EmailSentAt,
            EmailSentTo = invoice.EmailSentTo,
            EmailSendCount = invoice.EmailSendCount,
            EmailCcRecipients = invoice.EmailCcRecipients,
            EmailBccRecipients = invoice.EmailBccRecipients,
            PrintedAt = invoice.PrintedAt,
            PrintCount = invoice.PrintCount,
            DiscountType = invoice.DiscountType.ToString(),
            DiscountValue = invoice.DiscountValue,
            TotalNet = invoice.TotalNet,
            DiscountAmount = invoice.DiscountAmount,
            TotalNetAfterDiscount = invoice.TotalNetAfterDiscount,
            TotalVat = invoice.TotalVat,
            TotalGross = invoice.TotalGross,
            TotalDownPayments = invoice.TotalDownPayments,
            AmountDue = invoice.AmountDue,
            Items = invoice.Items.Select(item => new InvoiceItemDto
            {
                Id = item.Id,
                InvoiceId = item.InvoiceId,
                Position = item.Position,
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                VatRate = item.VatRate,
                TotalNet = item.TotalNet,
                TotalVat = item.TotalVat,
                TotalGross = item.TotalGross
            }).ToList(),
            DownPayments = invoice.DownPayments.Select(dp => new DownPaymentDto
            {
                Id = dp.Id,
                InvoiceId = dp.InvoiceId,
                Description = dp.Description,
                Amount = dp.Amount,
                PaymentDate = dp.PaymentDate,
                CreatedAt = dp.CreatedAt,
                SourceInvoiceId = dp.SourceInvoiceId,
                SourceInvoiceNumber = dp.SourceInvoiceId.HasValue && sourceInvoiceNumbers.TryGetValue(dp.SourceInvoiceId.Value, out var number)
                    ? number
                    : null
            }).ToList()
        };
    }
}

public record MarkAsPaidRequest(DateTime PaidDate);
public record CancelInvoiceRequest(string? Reason = null);
public record SendInvoiceRequest(
    string? RecipientEmail = null,
    string? CustomMessage = null,
    EmailAttachmentFormat Format = EmailAttachmentFormat.NormalPdf,
    string? CcEmails = null,
    string? BccEmails = null);
