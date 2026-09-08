using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;
using MudBlazor;
using Kuestencode.Core.Enums;
using Kuestencode.Core.Models;
using Kuestencode.Faktura.Models;
using Kuestencode.Faktura.Services;
using Kuestencode.Faktura.Shared;
using Kuestencode.Faktura.Shared.Components;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Acta;
using Kuestencode.Shared.Contracts.Rapport;
using Kuestencode.Shared.Contracts.Recepta;

namespace Kuestencode.Faktura.Pages.Invoices;

public partial class Edit
{
    [Parameter]
    public int Id { get; set; }

    [Inject]
    public IRapportApiClient RapportApiClient { get; set; } = null!;

    [Inject]
    public ModuleAvailabilityService AvailabilityService { get; set; } = null!;

    [Inject]
    public IActaApiClient ActaApiClient { get; set; } = null!;

    [Inject]
    public IReceptaApiClient ReceptaApiClient { get; set; } = null!;

    private bool _customerError;
    private string? _customerErrorText;
    private MudAutocomplete<Customer>? _customerAuto;
    private Invoice? _invoice;
    private Customer? _selectedCustomer;
    private Company? _company;
    private List<Customer> _customers = new();
    private DateTime? _invoiceDate;
    private DateTime? _servicePeriodStart;
    private DateTime? _servicePeriodEnd;
    private DateTime? _dueDate;
    private bool _loading = true;
    private bool _saving = false;
    private string? _errorMessage;
    private decimal _totalNet, _totalVat, _totalGross, _totalDownPayments, _amountDue;
    private decimal _discountAmount, _totalNetAfterDiscount;
    private bool _hasDiscount = false;
    private bool _isReverseCharge = false;
    private System.Globalization.CultureInfo _culture = new System.Globalization.CultureInfo("de-DE");
    private const long MaxAttachmentSize = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".csv"
    };

    private bool _attachTimesheet;
    private bool _timesheetUseInvoicePeriod = true;
    private DateTime? _timesheetFromDate;
    private DateTime? _timesheetToDate;
    private decimal? _timesheetHourlyRate;
    private string _timesheetTitle = "Tätigkeitsnachweis";
    private string? _timesheetFileName;
    private TimesheetAttachmentFormat _timesheetFormat = TimesheetAttachmentFormat.Pdf;
    private bool _timesheetGenerating;
    private bool _timesheetAttachmentAdded;
    private bool _rapportAvailable;
    private bool _actaAvailable;
    private bool _receptaAvailable;
    private bool _projectWorkflowAvailable;
    private List<ActaProjectDto> _projects = new();
    private List<ReceptaDocumentDto> _projectReceipts = new();
    private int? _selectedProjectExternalId;
    private Guid? _selectedProjectInternalId;
    private bool _loadingProjectReceipts;
    private bool _receiptAttachmentAdded;
    private readonly HashSet<Guid> _pendingReceiptAttachmentMarks = new();
    private List<Invoice> _downPaymentInvoiceCandidates = new();

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _invoice = await InvoiceService.GetByIdAsync(Id, includeCustomer: true, includeItems: true);

            if (_invoice == null)
            {
                _loading = false;
                return;
            }

            // Load customers and company data
            _customers = await CustomerService.GetAllAsync();
            _company = await CompanyService.GetCompanyAsync();

            // Set selected customer
            _selectedCustomer = _invoice.Customer;

            // Convert DateTime values for date pickers
            _invoiceDate = _invoice.InvoiceDate;
            _servicePeriodStart = _invoice.ServicePeriodStart;
            _servicePeriodEnd = _invoice.ServicePeriodEnd;
            _dueDate = _invoice.DueDate;

            // Initialize discount toggle
            _hasDiscount = _invoice.DiscountType != DiscountType.None;

            // Initialize reverse-charge toggle
            _isReverseCharge = _invoice.IsReverseCharge;

            // Initialize project selection from existing invoice
            _selectedProjectExternalId = _invoice.ProjectId;

            _downPaymentInvoiceCandidates = (await InvoiceService.GetByTypeAsync(InvoiceType.Invoice))
                .Where(i => i.Id != _invoice.Id)
                .ToList();

            SyncTimesheetRange();
            RecalculateTotals();
        }
        catch (Exception ex)
        {
            _errorMessage = $"Fehler beim Laden: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await CheckModuleAvailabilityAsync();
        if (!_rapportAvailable)
        {
            _attachTimesheet = false;
            _timesheetAttachmentAdded = false;
        }
        StateHasChanged();
    }

    private async Task CheckModuleAvailabilityAsync()
    {
        try
        {
            var availability = await AvailabilityService.CheckAsync();
            _rapportAvailable = availability.Rapport;
            _actaAvailable = availability.Acta;
            _receptaAvailable = availability.Recepta;
            _projectWorkflowAvailable = _rapportAvailable && _actaAvailable && _receptaAvailable;

            if (_projectWorkflowAvailable)
            {
                _projects = await ActaApiClient.GetProjectsAsync();
            }
            else
            {
                _projects.Clear();
                _projectReceipts.Clear();
                _selectedProjectExternalId = null;
                _selectedProjectInternalId = null;
                _receiptAttachmentAdded = false;
            }
        }
        catch
        {
            _rapportAvailable = false;
            _actaAvailable = false;
            _receptaAvailable = false;
            _projectWorkflowAvailable = false;
            _projects.Clear();
            _projectReceipts.Clear();
        }
    }

    private async Task OnProjectChanged(int? projectExternalId)
    {
        _selectedProjectExternalId = projectExternalId;
        _selectedProjectInternalId = null;
        _projectReceipts.Clear();
        _receiptAttachmentAdded = false;

        if (!projectExternalId.HasValue)
        {
            return;
        }

        var project = _projects.FirstOrDefault(p => p.Id == projectExternalId.Value);
        if (project == null)
        {
            return;
        }

        _selectedProjectInternalId = project.InternalProjectId;
        if (!_selectedProjectInternalId.HasValue || _selectedProjectInternalId.Value == Guid.Empty)
        {
            var fullProject = await ActaApiClient.GetProjectByExternalIdAsync(projectExternalId.Value);
            _selectedProjectInternalId = fullProject?.InternalProjectId;
        }
        _selectedCustomer = _customers.FirstOrDefault(c => c.Id == project.CustomerId);

        await LoadProjectReceiptsAsync();
    }

    private async Task<IEnumerable<Customer>> SearchCustomers(string value, CancellationToken token)
    {
        await Task.CompletedTask; // Async compatibility

        if (string.IsNullOrWhiteSpace(value))
            return _customers;

        return _customers.Where(c =>
            c.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            c.CustomerNumber.Contains(value, StringComparison.OrdinalIgnoreCase)
        );
    }

    private async Task OnCustomerCreated(Customer customer)
    {
        _customers = await CustomerService.GetAllAsync();
        _selectedCustomer = _customers.FirstOrDefault(c => c.Id == customer.Id) ?? customer;
        StateHasChanged();
    }

    private record ItemSection(InvoiceItem? Header, List<InvoiceItem> Items);

    private List<ItemSection> GetItemSections()
    {
        var result = new List<ItemSection>();
        InvoiceItem? currentHeader = null;
        var currentItems = new List<InvoiceItem>();

        foreach (var item in _invoice!.Items)
        {
            if (item.IsHeader)
            {
                result.Add(new ItemSection(currentHeader, currentItems));
                currentHeader = item;
                currentItems = new List<InvoiceItem>();
            }
            else
            {
                currentItems.Add(item);
            }
        }
        result.Add(new ItemSection(currentHeader, currentItems));
        return result.Where(s => s.Header != null || s.Items.Count > 0).ToList();
    }

    private void AddItem()
    {
        if (_invoice == null) return;

        decimal vatRate;
        if (_company?.IsKleinunternehmer == true)
            vatRate = 0;
        else if (_isReverseCharge)
            vatRate = 0;
        else
            vatRate = 19;
        _invoice.Items.Add(new InvoiceItem
        {
            Quantity = 1,
            UnitPrice = 0,
            VatRate = vatRate,
            InvoiceId = _invoice.Id
        });
        ReorderItems();
        RecalculateTotals();
    }

    private void AddHeader()
    {
        if (_invoice == null) return;

        _invoice.Items.Add(new InvoiceItem
        {
            IsHeader = true,
            Description = string.Empty,
            Quantity = 0,
            UnitPrice = 0,
            VatRate = 0,
            InvoiceId = _invoice.Id
        });
        ReorderItems();
    }

    private void RemoveItem(InvoiceItem item)
    {
        if (_invoice == null) return;

        var nonHeaderCount = _invoice.Items.Count(i => !i.IsHeader);
        if (item.IsHeader || nonHeaderCount > 1)
        {
            _invoice.Items.Remove(item);
            ReorderItems();
            RecalculateTotals();
        }
    }

    private void MoveItemUp(InvoiceItem item)
    {
        if (_invoice == null) return;

        var index = _invoice.Items.IndexOf(item);
        if (index > 0)
        {
            _invoice.Items.RemoveAt(index);
            _invoice.Items.Insert(index - 1, item);
            ReorderItems();
            RecalculateTotals();
        }
    }

    private void MoveItemDown(InvoiceItem item)
    {
        if (_invoice == null) return;

        var index = _invoice.Items.IndexOf(item);
        if (index < _invoice.Items.Count - 1)
        {
            _invoice.Items.RemoveAt(index);
            _invoice.Items.Insert(index + 1, item);
            ReorderItems();
            RecalculateTotals();
        }
    }

    private void ReorderItems()
    {
        if (_invoice == null) return;

        for (int i = 0; i < _invoice.Items.Count; i++)
        {
            _invoice.Items[i].Position = i + 1;
        }
    }

    private void AddDownPayment()
    {
        if (_invoice == null) return;

        _invoice.DownPayments.Add(new DownPayment
        {
            Description = string.Empty,
            Amount = 0,
            PaymentDate = null,
            InvoiceId = _invoice.Id
        });
        StateHasChanged();
    }

    private void RemoveDownPayment(DownPayment downPayment)
    {
        if (_invoice == null) return;

        _invoice.DownPayments.Remove(downPayment);
        RecalculateTotals();
    }

    private void OnDownPaymentSourceInvoiceChanged(DownPayment downPayment)
    {
        downPayment.SourceInvoiceId = downPayment.SourceInvoice?.Id;

        if (downPayment.SourceInvoice == null) return;

        if (string.IsNullOrWhiteSpace(downPayment.Description))
            downPayment.Description = downPayment.SourceInvoice.InvoiceNumber;

        if (downPayment.Amount == 0)
            downPayment.Amount = downPayment.SourceInvoice.TotalGross;

        RecalculateTotals();
    }

    private async Task<IEnumerable<Invoice>> SearchDownPaymentInvoices(string value, CancellationToken token)
    {
        await Task.CompletedTask;

        if (_selectedCustomer == null)
            return [];

        var candidates = _downPaymentInvoiceCandidates.Where(i => i.CustomerId == _selectedCustomer.Id);

        if (string.IsNullOrWhiteSpace(value))
            return candidates;

        return candidates.Where(i =>
            i.InvoiceNumber.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private void AddDiscount()
    {
        if (_invoice == null) return;
        _hasDiscount = true;
        _invoice.DiscountType = DiscountType.Percentage;
        _invoice.DiscountValue = 0;
        RecalculateTotals();
    }

    private void RemoveDiscount()
    {
        if (_invoice == null) return;
        _hasDiscount = false;
        _invoice.DiscountType = DiscountType.None;
        _invoice.DiscountValue = null;
        RecalculateTotals();
    }

    private void AddTimesheet()
    {
        _attachTimesheet = true;
    }

    private void RemoveTimesheet()
    {
        _attachTimesheet = false;
    }

    private void OnReverseChargeToggle(bool value)
    {
        if (_invoice == null) return;
        _isReverseCharge = value;
        _invoice.IsReverseCharge = value;
        RecalculateTotals();
    }

    private void OnServicePeriodChanged()
    {
        if (_timesheetUseInvoicePeriod)
        {
            SyncTimesheetRange();
        }
    }

    private void SyncTimesheetRange()
    {
        var from = _servicePeriodStart ?? _invoiceDate ?? DateTime.Today;
        var to = _servicePeriodEnd ?? _servicePeriodStart ?? _invoiceDate ?? DateTime.Today;
        _timesheetFromDate = from;
        _timesheetToDate = to;
    }

    private void RecalculateTotals()
    {
        if (_invoice == null) return;

        // Ensure all items have the correct VAT rate based on company settings
        decimal vatRate;
        if (_company?.IsKleinunternehmer == true)
            vatRate = 0;
        else if (_isReverseCharge)
            vatRate = 0;
        else
            vatRate = 19;
        foreach (var item in _invoice.Items)
        {
            item.VatRate = vatRate;
        }

        _totalNet = _invoice.Items.Sum(i => i.TotalNet);

        // Calculate discount
        if (_invoice.DiscountType != DiscountType.None && _invoice.DiscountValue.HasValue)
        {
            if (_invoice.DiscountType == DiscountType.Percentage)
            {
                _discountAmount = _totalNet * (_invoice.DiscountValue.Value / 100);
            }
            else
            {
                _discountAmount = _invoice.DiscountValue.Value;
            }
        }
        else
        {
            _discountAmount = 0;
        }

        _totalNetAfterDiscount = _totalNet - _discountAmount;

        // Calculate VAT proportionally after discount
        if (_totalNet > 0)
        {
            var discountRatio = _totalNetAfterDiscount / _totalNet;
            _totalVat = _invoice.Items.Sum(i => i.TotalVat) * discountRatio;
        }
        else
        {
            _totalVat = 0;
        }

        _totalGross = _totalNetAfterDiscount + _totalVat;
        _totalDownPayments = _invoice.DownPayments.Sum(d => d.Amount);
        _amountDue = _totalGross - _totalDownPayments;
        StateHasChanged();
    }

    private async Task SaveAsync(bool asDraft)
    {
        if (_invoice == null) return;

        _customerError = false;
        _customerErrorText = null;
        _errorMessage = null;

        // Validate customer
        if (_selectedCustomer == null)
        {
            _customerError = true;
            _customerErrorText = "Kunde auswählen";

            if (_customerAuto is not null)
                await _customerAuto.FocusAsync();

            return;
        }

        // Validate items
        var nonHeaderItems = _invoice.Items.Where(i => !i.IsHeader).ToList();
        if (nonHeaderItems.Count == 0 || nonHeaderItems.All(i => string.IsNullOrWhiteSpace(i.Description)))
        {
            _errorMessage = "Mindestens eine Position angeben.";
            return;
        }

        if (nonHeaderItems.Any(i => i.Quantity <= 0))
        {
            _errorMessage = "Menge muss > 0 sein.";
            return;
        }

        // Validate invoice date
        if (_invoiceDate == null)
        {
            _errorMessage = "Rechnungsdatum fehlt.";
            return;
        }

        if (_invoiceDate.Value > DateTime.Today)
        {
            _errorMessage = "Rechnungsdatum darf nicht in der Zukunft liegen.";
            return;
        }

        // Abschläge: Validierung
        if (_invoice.DownPayments.Any(d => d.Amount <= 0))
        {
            _errorMessage = "Abschlag-Betrag muss größer als 0 sein.";
            return;
        }

        if (_invoice.DownPayments.Any(d => string.IsNullOrWhiteSpace(d.Description)))
        {
            _errorMessage = "Beschreibung für Abschläge ist erforderlich.";
            return;
        }

        if (_totalDownPayments > Math.Abs(_totalGross))
        {
            _errorMessage = "Abschläge dürfen die Bruttosumme nicht übersteigen.";
            return;
        }

        // Rabatt: Validierung
        if (_invoice.DiscountType != DiscountType.None)
        {
            if (!_invoice.DiscountValue.HasValue || _invoice.DiscountValue.Value <= 0)
            {
                _errorMessage = "Rabatt muss größer als 0 sein.";
                return;
            }

            if (_invoice.DiscountType == DiscountType.Percentage && _invoice.DiscountValue.Value > 100)
            {
                _errorMessage = "Prozentualer Rabatt darf nicht über 100% liegen.";
                return;
            }

            if (_invoice.DiscountType == DiscountType.Absolute && _invoice.DiscountValue.Value > Math.Abs(_totalNet))
            {
                _errorMessage = "Absoluter Rabatt darf die Nettosumme nicht übersteigen.";
                return;
            }
        }

        _saving = true;

        try
        {
            _invoice.CustomerId = _selectedCustomer.Id;
            _invoice.ProjectId = _selectedProjectExternalId;
            _invoice.InvoiceDate = DateTime.SpecifyKind(_invoiceDate.Value, DateTimeKind.Utc);
            _invoice.ServicePeriodStart = _servicePeriodStart.HasValue ? DateTime.SpecifyKind(_servicePeriodStart.Value, DateTimeKind.Utc) : null;
            _invoice.ServicePeriodEnd = _servicePeriodEnd.HasValue ? DateTime.SpecifyKind(_servicePeriodEnd.Value, DateTimeKind.Utc) : null;
            _invoice.DueDate = _dueDate.HasValue ? DateTime.SpecifyKind(_dueDate.Value, DateTimeKind.Utc) : null;

            if (_attachTimesheet)
            {
                var ok = await EnsureTimesheetAttachmentAsync();
                if (!ok)
                {
                    _saving = false;
                    return;
                }
            }

            // Set invoice status based on asDraft parameter
            _invoice.Status = asDraft ? InvoiceStatus.Draft : InvoiceStatus.Sent;

            await InvoiceService.UpdateAsync(_invoice);
            await MarkPendingReceiptAttachmentsAsync();

            var statusText = asDraft ? "als Entwurf gespeichert" : "versendet";
            Snackbar.Add($"Faktura {_invoice.InvoiceNumber} wurde {statusText}.", Severity.Success);

            NavigationManager.NavigateTo($"/faktura/invoices/details/{_invoice.Id}");
        }
        catch (Exception ex)
        {
            _errorMessage = $"Fehler beim Speichern: {ex.Message}";
        }
        finally
        {
            _saving = false;
        }
    }

    private void Cancel()
    {
        NavigationManager.NavigateTo($"/faktura/invoices");
    }

    private async Task HandleAttachmentUpload(IReadOnlyList<IBrowserFile> files)
    {
        if (_invoice == null) return;

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.Name);
            if (!AllowedAttachmentExtensions.Contains(extension))
            {
                Snackbar.Add($"Dateityp nicht erlaubt: {file.Name}", Severity.Error);
                continue;
            }

            if (file.Size > MaxAttachmentSize)
            {
                Snackbar.Add($"Datei zu groß: {file.Name}", Severity.Error);
                continue;
            }

            try
            {
                using var memoryStream = new MemoryStream();
                await file.OpenReadStream(MaxAttachmentSize).CopyToAsync(memoryStream);

                _invoice.Attachments.Add(new InvoiceAttachment
                {
                    FileName = file.Name,
                    ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                        ? "application/octet-stream"
                        : file.ContentType,
                    FileSize = file.Size,
                    Data = memoryStream.ToArray()
                });
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Fehler beim Upload: {file.Name} ({ex.Message})", Severity.Error);
            }
        }
    }

    private async Task CreateTimesheetAttachmentAsync()
    {
        var ok = await EnsureTimesheetAttachmentAsync();
        if (ok)
        {
            Snackbar.Add("Tätigkeitsnachweis wurde als Anhang hinzugefügt.", Severity.Success);
        }
    }

    private async Task<bool> EnsureTimesheetAttachmentAsync()
    {
        if (_timesheetAttachmentAdded)
        {
            return true;
        }

        if (!_rapportAvailable)
        {
            Snackbar.Add("Rapport Modul ist nicht registriert. Tätigkeitsnachweis kann nicht angehängt werden.", Severity.Warning);
            return false;
        }

        if (_invoice == null || _selectedCustomer == null)
        {
            Snackbar.Add("Bitte zuerst einen Kunden auswählen.", Severity.Warning);
            return false;
        }

        if (_timesheetUseInvoicePeriod)
        {
            SyncTimesheetRange();
        }

        if (!_timesheetFromDate.HasValue || !_timesheetToDate.HasValue)
        {
            Snackbar.Add("Bitte einen Zeitraum für den Tätigkeitsnachweis auswählen.", Severity.Warning);
            return false;
        }

        var request = new TimesheetExportRequestDto
        {
            CustomerId = _selectedCustomer.Id,
            From = _timesheetFromDate.Value.Date,
            To = _timesheetToDate.Value.Date.AddDays(1).AddTicks(-1),
            ProjectId = _selectedProjectExternalId,
            HourlyRate = _timesheetHourlyRate,
            Title = _timesheetTitle,
            FileName = string.IsNullOrWhiteSpace(_timesheetFileName) ? null : _timesheetFileName.Trim()
        };

        _timesheetGenerating = true;
        try
        {
            byte[] bytes;
            string contentType;

            if (_timesheetFormat == TimesheetAttachmentFormat.Csv)
            {
                bytes = await RapportApiClient.GenerateTimesheetCsvAsync(request);
                contentType = "text/csv";
            }
            else
            {
                bytes = await RapportApiClient.GenerateTimesheetPdfAsync(request);
                contentType = "application/pdf";
            }

            var fileName = BuildTimesheetFileName(request, _selectedCustomer.Name, _timesheetFormat);

            _invoice.Attachments.Add(new InvoiceAttachment
            {
                FileName = fileName,
                ContentType = contentType,
                FileSize = bytes.Length,
                Data = bytes
            });

            _timesheetAttachmentAdded = true;
            return true;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Erstellen des Tätigkeitsnachweises: {ex.Message}", Severity.Error);
            return false;
        }
        finally
        {
            _timesheetGenerating = false;
        }
    }

    private async Task AddReceiptAttachmentAsync()
    {
        var ok = await EnsureReceiptAttachmentAsync();
        if (ok)
        {
            Snackbar.Add("Belegliste wurde als Anhang hinzugefügt.", Severity.Success);
        }
    }

    private async Task<bool> EnsureReceiptAttachmentAsync()
    {
        if (_invoice == null)
        {
            return false;
        }

        if (_receiptAttachmentAdded)
        {
            return true;
        }

        if (!_projectWorkflowAvailable || !_selectedProjectExternalId.HasValue || !_selectedProjectInternalId.HasValue)
        {
            var project = await ActaApiClient.GetProjectByExternalIdAsync(_selectedProjectExternalId ?? 0);
            _selectedProjectInternalId = project?.InternalProjectId;
            if (!_selectedProjectInternalId.HasValue)
            {
                return false;
            }
        }

        if (_projectReceipts.Count == 0)
        {
            await LoadProjectReceiptsAsync();
        }

        var csv = BuildReceiptsCsv(_projectReceipts);
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
        var selectedExternalId = _selectedProjectExternalId ?? 0;
        var selectedProject = _projects.FirstOrDefault(p => p.Id == selectedExternalId);
        var safeProjectName = string.IsNullOrWhiteSpace(selectedProject?.Name) ? $"Projekt_{selectedExternalId}" : selectedProject!.Name.Trim();
        var fileName = SanitizeFileName($"Belegliste_{safeProjectName}_{DateTime.UtcNow:yyyyMM}.csv");

        if (!HasAttachmentWithFileName(fileName))
        {
            _invoice.Attachments.Add(new InvoiceAttachment
            {
                FileName = fileName,
                ContentType = "text/csv",
                FileSize = bytes.Length,
                Data = bytes
            });
        }

        foreach (var receipt in _projectReceipts)
        {
            List<ReceptaDocumentFileDto> files;
            try
            {
                files = await ReceptaApiClient.GetFilesByDocumentAsync(receipt.Id);
            }
            catch
            {
                continue;
            }

            foreach (var file in files.Where(IsPdfFile))
            {
                var download = await ReceptaApiClient.DownloadFileAsync(file.Id);
                if (download == null || download.Value.Data.Length == 0)
                {
                    continue;
                }

                var attachmentName = BuildReceiptPdfAttachmentFileName(receipt, download.Value.FileName);
                if (!HasAttachmentWithFileName(attachmentName))
                {
                    _invoice.Attachments.Add(new InvoiceAttachment
                    {
                        FileName = attachmentName,
                        ContentType = "application/pdf",
                        FileSize = download.Value.Data.Length,
                        Data = download.Value.Data
                    });
                }
            }
        }

        foreach (var receiptId in _projectReceipts.Select(r => r.Id))
        {
            _pendingReceiptAttachmentMarks.Add(receiptId);
        }

        _receiptAttachmentAdded = true;
        return true;
    }

    private async Task MarkPendingReceiptAttachmentsAsync()
    {
        if (_pendingReceiptAttachmentMarks.Count == 0)
        {
            return;
        }

        var ids = _pendingReceiptAttachmentMarks.ToArray();
        _pendingReceiptAttachmentMarks.Clear();

        var markSuccess = await ReceptaApiClient.MarkDocumentsAsAttachedAsync(ids);
        if (!markSuccess)
        {
            Snackbar.Add("Belege konnten nicht als angehängt markiert werden. Bitte erneut versuchen.", Severity.Warning);
        }
    }

    private static string BuildReceiptsCsv(IEnumerable<ReceptaDocumentDto> receipts)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Interne Belegnummer;Lieferant;Rechnungsnummer;Rechnungsdatum;Betrag Netto;Betrag Brutto");

        foreach (var receipt in receipts)
        {
            sb.Append(EscapeCsv(receipt.DocumentNumber)).Append(';')
              .Append(EscapeCsv(receipt.SupplierName)).Append(';')
              .Append(EscapeCsv(receipt.InvoiceNumber)).Append(';')
              .Append(receipt.InvoiceDate.ToString("dd.MM.yyyy")).Append(';')
              .Append(receipt.AmountNet.ToString("F2", CultureInfo.GetCultureInfo("de-DE"))).Append(';')
              .Append(receipt.AmountGross.ToString("F2", CultureInfo.GetCultureInfo("de-DE"))).AppendLine();
        }

        return sb.ToString();
    }

    private static bool IsPdfFile(ReceptaDocumentFileDto file)
    {
        if (file.FileSize <= 0)
        {
            return false;
        }

        if (file.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildReceiptPdfAttachmentFileName(ReceptaDocumentDto receipt, string sourceFileName)
    {
        var suffix = string.IsNullOrWhiteSpace(sourceFileName) ? "beleg.pdf" : sourceFileName.Trim();
        return SanitizeFileName($"{receipt.DocumentNumber}_{suffix}");
    }

    private async Task LoadProjectReceiptsAsync()
    {
        _loadingProjectReceipts = true;
        try
        {
            var receiptsById = new Dictionary<Guid, ReceptaDocumentDto>();
            foreach (var lookupProjectId in GetProjectLookupIds())
            {
                List<ReceptaDocumentDto> receipts;
                try
                {
                    receipts = await ReceptaApiClient.GetDocumentsByProjectAsync(lookupProjectId, onlyUnattached: true);
                }
                catch
                {
                    continue;
                }

                foreach (var receipt in receipts)
                {
                    receiptsById[receipt.Id] = receipt;
                }
            }

            _projectReceipts = receiptsById.Values
                .OrderBy(r => r.DocumentNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _loadingProjectReceipts = false;
        }
    }

    private IEnumerable<Guid> GetProjectLookupIds()
    {
        if (_selectedProjectInternalId.HasValue && _selectedProjectInternalId.Value != Guid.Empty)
        {
            yield return _selectedProjectInternalId.Value;
        }

        if (_selectedProjectExternalId.HasValue)
        {
            yield return BuildLegacyProjectGuid(_selectedProjectExternalId.Value);
        }
    }

    private static Guid BuildLegacyProjectGuid(int externalProjectId)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(externalProjectId).CopyTo(bytes, 0);
        bytes[4] = 0xAC;
        bytes[5] = 0x7A;
        return new Guid(bytes);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (cleaned.Contains(';') || cleaned.Contains('"'))
        {
            cleaned = '"' + cleaned.Replace("\"", "\"\"") + '"';
        }

        return cleaned;
    }

    private bool HasAttachmentWithFileName(string fileName)
    {
        if (_invoice == null)
        {
            return false;
        }

        return _invoice.Attachments.Any(a =>
            string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildTimesheetFileName(TimesheetExportRequestDto request, string customerName, TimesheetAttachmentFormat format)
    {
        var extension = format == TimesheetAttachmentFormat.Csv ? ".csv" : ".pdf";

        if (!string.IsNullOrWhiteSpace(request.FileName))
        {
            var raw = request.FileName.Trim();
            if (!raw.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                raw += extension;
            }

            return SanitizeFileName(raw);
        }

        var title = string.IsNullOrWhiteSpace(request.Title) ? "Tätigkeitsnachweis" : request.Title.Trim();
        var period = request.From.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var baseName = $"{title}_{customerName}_{period}{extension}";
        return SanitizeFileName(baseName);
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName;
    }

    private void RemoveAttachment(InvoiceAttachment attachment)
    {
        _invoice?.Attachments.Remove(attachment);
    }

    private static string FormatFileSize(long size)
    {
        const long kb = 1024;
        const long mb = 1024 * 1024;

        if (size >= mb)
        {
            return $"{size / (double)mb:F1} MB";
        }

        if (size >= kb)
        {
            return $"{size / (double)kb:F1} KB";
        }

        return $"{size} B";
    }

    private enum TimesheetAttachmentFormat
    {
        Pdf,
        Csv
    }
}



