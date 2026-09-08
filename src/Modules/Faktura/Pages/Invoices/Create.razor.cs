using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
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
using Kuestencode.Shared.Contracts.Offerte;
using Kuestencode.Shared.Contracts.Rapport;
using Kuestencode.Shared.Contracts.Recepta;

namespace Kuestencode.Faktura.Pages.Invoices;

public partial class Create
{
    [Inject]
    public IRapportApiClient RapportApiClient { get; set; } = null!;

    [Inject]
    public ModuleAvailabilityService AvailabilityService { get; set; } = null!;

    [Inject]
    public IActaApiClient ActaApiClient { get; set; } = null!;

    [Inject]
    public IReceptaApiClient ReceptaApiClient { get; set; } = null!;

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = null!;

    private bool _customerError;
    private string? _customerErrorText;
    private MudAutocomplete<Customer>? _customerAuto;
    private Invoice _invoice = new();
    private string _invoiceNumberPrefix = string.Empty;
    private string _invoiceNumberSuffix = string.Empty;
    private string _invoiceNumberSequence = string.Empty;
    private Customer? _selectedCustomer;
    private Company? _company;
    private List<Customer> _customers = new();
    private DateTime? _invoiceDate = DateTime.Today;
    private DateTime? _servicePeriodStart;
    private DateTime? _servicePeriodEnd;
    private DateTime? _dueDate;
    private bool _saving = false;
    private string? _errorMessage;
    private decimal _totalNet, _totalVat, _totalGross, _totalDownPayments, _amountDue;
    private List<Invoice> _downPaymentInvoiceCandidates = new();
    private decimal _discountAmount, _totalNetAfterDiscount;
    private bool _enableDiscount = false;
    private bool _isReverseCharge = false;
    private bool _discountWarning = false;
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

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var (prefix, suffix, _) = await InvoiceService.GetInvoiceNumberFormatPartsAsync();
            _invoiceNumberPrefix = prefix;
            _invoiceNumberSuffix = suffix;

            _invoice.InvoiceNumber = await InvoiceService.GenerateInvoiceNumberAsync();
            _invoiceNumberSequence = _invoice.InvoiceNumber.Substring(
                _invoiceNumberPrefix.Length,
                _invoice.InvoiceNumber.Length - _invoiceNumberPrefix.Length - _invoiceNumberSuffix.Length);

            _customers = await CustomerService.GetAllAsync();
            _company = await CompanyService.GetCompanyAsync();
            var paymentDays = _company?.DefaultPaymentTermDays ?? 14;
            _dueDate = DateTime.Today.AddDays(paymentDays);
            _downPaymentInvoiceCandidates = await InvoiceService.GetByTypeAsync(InvoiceType.Invoice);
            AddItem();
            SyncTimesheetRange();
            RecalculateTotals();

            // Prüfe, ob Daten aus Offerte-Modul übernommen werden sollen
            await LoadOfferteDataAsync();
        }
        catch (Exception ex)
        {
            _errorMessage = $"Fehler beim Initialisieren: {ex.Message}";
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

    private async Task LoadOfferteDataAsync()
    {
        try
        {
            // Prüfe Query-Parameter
            var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
            if (!QueryHelpers.ParseQuery(uri.Query).TryGetValue("from", out var fromValue) ||
                !string.Equals(fromValue, "offerte", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Lade DTO aus localStorage
            var json = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", "offerte_rechnung_dto");
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var dto = JsonSerializer.Deserialize<RechnungErstellungDto>(json);
            if (dto == null)
            {
                return;
            }

            // Lösche localStorage-Eintrag nach dem Lesen
            await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "offerte_rechnung_dto");

            // Kunde setzen
            _selectedCustomer = _customers.FirstOrDefault(c => c.Id == dto.KundeId);

            // Referenz setzen
            if (!string.IsNullOrWhiteSpace(dto.Referenz))
            {
                _invoice.Notes = dto.Referenz;
            }

            // Positionen übernehmen
            if (dto.Positionen.Count > 0)
            {
                _invoice.Items.Clear();
                decimal vatRate;
                if (_company?.IsKleinunternehmer == true)
                    vatRate = 0;
                else if (_isReverseCharge)
                    vatRate = 0;
                else
                    vatRate = 19;

                foreach (var pos in dto.Positionen)
                {
                    _invoice.Items.Add(new InvoiceItem
                    {
                        Description = pos.Text,
                        Quantity = pos.IsHeader ? 0 : pos.Menge,
                        UnitPrice = pos.IsHeader ? 0 : pos.Einzelpreis,
                        VatRate = pos.IsHeader ? 0 : vatRate,
                        IsHeader = pos.IsHeader,
                        Unit = pos.Einheit
                    });
                }

                RecalculateTotals();
            }
        }
        catch (Exception ex)
        {
            // Fehler beim Laden ignorieren - Benutzer kann manuell fortfahren
            Console.WriteLine($"Fehler beim Laden der Offerte-Daten: {ex.Message}");
        }
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

    private void UpdateInvoiceNumber()
    {
        _invoice.InvoiceNumber = _invoiceNumberPrefix + _invoiceNumberSequence + _invoiceNumberSuffix;
    }

    private string? ValidateInvoiceNumberSequence(string sequence)
    {
        if (string.IsNullOrWhiteSpace(sequence))
        {
            return "Laufende Nummer ist erforderlich";
        }

        return sequence.All(char.IsDigit) ? null : "Nur Ziffern erlaubt";
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

        foreach (var item in _invoice.Items)
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
            VatRate = vatRate
        });
        ReorderItems();
    }

    private void AddHeader()
    {
        _invoice.Items.Add(new InvoiceItem
        {
            IsHeader = true,
            Description = string.Empty,
            Quantity = 0,
            UnitPrice = 0,
            VatRate = 0
        });
        ReorderItems();
    }

    private void RemoveItem(InvoiceItem item)
    {
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
        for (int i = 0; i < _invoice.Items.Count; i++)
        {
            _invoice.Items[i].Position = i + 1;
        }
    }

    private void AddDownPayment()
    {
        _invoice.DownPayments.Add(new DownPayment
        {
            Description = string.Empty,
            Amount = 0,
            PaymentDate = null
        });
        StateHasChanged();
    }

    private void RemoveDownPayment(DownPayment downPayment)
    {
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
        _enableDiscount = true;
        _invoice.DiscountType = DiscountType.Percentage;
        _invoice.DiscountValue = 0;
        RecalculateTotals();
    }

    private void RemoveDiscount()
    {
        _enableDiscount = false;
        _invoice.DiscountType = DiscountType.None;
        _invoice.DiscountValue = null;
        _discountWarning = false;
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

    private string GetDiscountLabel()
    {
        if (_invoice.DiscountType == DiscountType.Percentage && _invoice.DiscountValue.HasValue)
        {
            return $"Rabatt ({_invoice.DiscountValue.Value:N2}%)";
        }
        return "Rabatt";
    }

    private void RecalculateTotals()
    {
        // Ensure all items have the correct VAT rate based on company settings
        decimal vatRate;
        if (_company?.IsKleinunternehmer == true)
            vatRate = 0;
        else if (_isReverseCharge)
            vatRate = 0;
        else
            vatRate = 19;
        foreach (var item in _invoice.Items.Where(i => !i.IsHeader))
        {
            item.VatRate = vatRate;
        }

        _totalNet = _invoice.TotalNet;
        _discountAmount = _invoice.DiscountAmount;
        _totalNetAfterDiscount = _invoice.TotalNetAfterDiscount;
        _totalVat = _invoice.TotalVat;
        _totalGross = _invoice.TotalGross;
        _totalDownPayments = _invoice.TotalDownPayments;
        _amountDue = _invoice.AmountDue;

        // Check for discount warning
        if (_invoice.DiscountType == DiscountType.Percentage &&
            _invoice.DiscountValue.HasValue &&
            _invoice.DiscountValue.Value > 50)
        {
            _discountWarning = true;
        }
        else if (_invoice.DiscountType == DiscountType.Absolute &&
                 _totalNet > 0 &&
                 _discountAmount / _totalNet > 0.5m)
        {
            _discountWarning = true;
        }
        else
        {
            _discountWarning = false;
        }

        StateHasChanged();
    }

    private async Task<bool> ValidateInvoiceAsync()
    {
        _customerError = false;
        _customerErrorText = null;
        _errorMessage = null;

        if (ValidateInvoiceNumberSequence(_invoiceNumberSequence) != null)
        {
            _errorMessage = "Rechnungsnummer: Laufende Nummer darf nur Ziffern enthalten.";
            return false;
        }

        // Regel: Kunde ist Pflicht
        if (_selectedCustomer == null)
        {
            _customerError = true;
            _customerErrorText = "Kunde auswählen";

            if (_customerAuto is not null)
                await _customerAuto.FocusAsync();

            return false;
        }

        // Positionen: Pflicht
        var nonHeaderItems = _invoice.Items.Where(i => !i.IsHeader).ToList();
        if (nonHeaderItems.Count == 0 || nonHeaderItems.All(i => string.IsNullOrWhiteSpace(i.Description)))
        {
            _errorMessage = "Mindestens eine Position angeben.";
            return false;
        }

        if (nonHeaderItems.Any(i => i.Quantity <= 0))
        {
            _errorMessage = "Menge muss > 0 sein.";
            return false;
        }

        // Rechnungsdatum: Pflicht
        if (_invoiceDate == null)
        {
            _errorMessage = "Rechnungsdatum fehlt.";
            return false;
        }

        if (_invoiceDate.Value > DateTime.Today)
        {
            _errorMessage = "Rechnungsdatum darf nicht in der Zukunft liegen.";
            return false;
        }

        // Rabatt: Validierung
        if (_enableDiscount && _invoice.DiscountValue.HasValue)
        {
            if (_invoice.DiscountType == DiscountType.Percentage)
            {
                if (_invoice.DiscountValue.Value < 0 || _invoice.DiscountValue.Value > 100)
                {
                    _errorMessage = "Rabatt in Prozent muss zwischen 0 und 100 liegen.";
                    return false;
                }
            }
            else if (_invoice.DiscountType == DiscountType.Absolute)
            {
                if (_invoice.DiscountValue.Value <= 0)
                {
                    _errorMessage = "Rabatt-Betrag muss größer als 0 sein.";
                    return false;
                }
                if (_invoice.DiscountValue.Value > Math.Abs(_totalNet))
                {
                    _errorMessage = "Rabatt-Betrag darf die Nettosumme nicht übersteigen.";
                    return false;
                }
            }
        }

        // Abschläge: Validierung
        if (_invoice.DownPayments.Any(d => d.Amount <= 0))
        {
            _errorMessage = "Abschlag-Betrag muss größer als 0 sein.";
            return false;
        }

        if (_invoice.DownPayments.Any(d => string.IsNullOrWhiteSpace(d.Description)))
        {
            _errorMessage = "Beschreibung für Abschläge ist erforderlich.";
            return false;
        }

        if (_totalDownPayments > Math.Abs(_totalGross))
        {
            _errorMessage = "Abschläge dürfen die Bruttosumme nicht übersteigen.";
            return false;
        }

        return true;
    }

    private async Task SaveAsync(bool asDraft)
    {
        if (!await ValidateInvoiceAsync())
            return;

        _saving = true;

        try
        {
            if (_selectedCustomer != null)
                _invoice.CustomerId = _selectedCustomer.Id;

            _invoice.ProjectId = _selectedProjectExternalId;

            var invoiceDate = _invoiceDate ?? throw new InvalidOperationException("Rechnungsdatum fehlt.");
            _invoice.InvoiceDate = DateTime.SpecifyKind(invoiceDate, DateTimeKind.Utc);
            _invoice.ServicePeriodStart = _servicePeriodStart.HasValue ? DateTime.SpecifyKind(_servicePeriodStart.Value, DateTimeKind.Utc) : null;
            _invoice.ServicePeriodEnd   = _servicePeriodEnd.HasValue ? DateTime.SpecifyKind(_servicePeriodEnd.Value, DateTimeKind.Utc) : null;
            _invoice.DueDate            = _dueDate.HasValue ? DateTime.SpecifyKind(_dueDate.Value, DateTimeKind.Utc) : null;

            if (_attachTimesheet)
            {
                var ok = await EnsureTimesheetAttachmentAsync();
                if (!ok)
                {
                    _saving = false;
                    return;
                }
            }

            _invoice.Status = asDraft ? InvoiceStatus.Draft : InvoiceStatus.Sent;

            await InvoiceService.CreateAsync(_invoice);
            await MarkPendingReceiptAttachmentsAsync();

            Snackbar.Add($"Rechnung {_invoice.InvoiceNumber} wurde erfolgreich erstellt.",
                Severity.Success);

            NavigationManager.NavigateTo("/faktura/invoices");
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

    private async Task SaveAndSendAsync(bool asDraft = false)
    {
        if (!await ValidateInvoiceAsync())
            return;

        // E-Mail-Konfiguration prüfen
        var isEmailConfigured = await CompanyService.IsEmailConfiguredAsync();
        if (!isEmailConfigured)
        {
            _errorMessage = "E-Mail-Versand ist nicht konfiguriert. Bitte richten Sie zuerst die E-Mail-Einstellungen ein.";
            return;
        }

        _saving = true;

        try
        {
            if (_selectedCustomer != null)
                _invoice.CustomerId = _selectedCustomer.Id;

            _invoice.ProjectId = _selectedProjectExternalId;

            var invoiceDate = _invoiceDate ?? throw new InvalidOperationException("Rechnungsdatum fehlt.");
            _invoice.InvoiceDate = DateTime.SpecifyKind(invoiceDate, DateTimeKind.Utc);
            _invoice.ServicePeriodStart = _servicePeriodStart.HasValue ? DateTime.SpecifyKind(_servicePeriodStart.Value, DateTimeKind.Utc) : null;
            _invoice.ServicePeriodEnd   = _servicePeriodEnd.HasValue ? DateTime.SpecifyKind(_servicePeriodEnd.Value, DateTimeKind.Utc) : null;
            _invoice.DueDate            = _dueDate.HasValue ? DateTime.SpecifyKind(_dueDate.Value, DateTimeKind.Utc) : null;

            if (_attachTimesheet)
            {
                var ok = await EnsureTimesheetAttachmentAsync();
                if (!ok)
                {
                    _saving = false;
                    return;
                }
            }

            _invoice.Status = InvoiceStatus.Sent;

            var createdInvoice = await InvoiceService.CreateAsync(_invoice);
            await MarkPendingReceiptAttachmentsAsync();

            Snackbar.Add($"Rechnung {_invoice.InvoiceNumber} wurde erfolgreich erstellt.", Severity.Success);

            // E-Mail-Dialog öffnen
            await OpenEmailDialog(createdInvoice);
        }
        catch (Exception ex)
        {
            _errorMessage = $"Fehler beim Speichern: {ex.Message}";
            _saving = false;
        }
    }

    private async Task OpenEmailDialog(Invoice invoice)
    {
        var parameters = new DialogParameters
        {
            [nameof(SendEmailDialog.Invoice)] = invoice,
            [nameof(SendEmailDialog.CustomerEmail)] = _selectedCustomer?.Email,
            [nameof(SendEmailDialog.OnSend)] =
                EventCallback.Factory.Create<(string Email, string? Message, EmailAttachmentFormat Format, string? CcEmails, string? BccEmails, bool IncludeClosing)>(
                    this, SendInvoiceEmail)
        };

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await DialogService.ShowAsync<SendEmailDialog>("E-Mail versenden", parameters, options);
        var result = await dialog.Result;

        // zurück zur Rechnungsliste
        _saving = false;
        NavigationManager.NavigateTo("/faktura/invoices");
    }

    private async Task SendInvoiceEmail((string Email, string? Message, EmailAttachmentFormat Format, string? CcEmails, string? BccEmails, bool IncludeClosing) data)
    {
        if (_invoice == null) return;

        try
        {
            var success = await EmailService.SendInvoiceEmailAsync(
                _invoice.Id,
                data.Email,
                data.Message,
                data.Format,
                data.CcEmails,
                data.BccEmails,
                data.IncludeClosing);

            if (success)
            {
                var formatText = data.Format switch
                {
                    EmailAttachmentFormat.ZugferdPdf => " als ZUGFeRD-PDF",
                    EmailAttachmentFormat.XRechnungXmlOnly => " als XRechnung-XML",
                    EmailAttachmentFormat.XRechnungXmlAndPdf => " als XRechnung (XML + PDF)",
                    _ => ""
                };

                Snackbar.Add(
                    $"Rechnung {_invoice.InvoiceNumber}{formatText} wurde erfolgreich an {data.Email} versendet.",
                    Severity.Success);
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Versenden der E-Mail: {ex.Message}", Severity.Error);
            throw;
        }
    }

    private void Cancel()
    {
        NavigationManager.NavigateTo("/faktura/invoices");
    }

    private async Task HandleAttachmentUpload(IReadOnlyList<IBrowserFile> files)
    {
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

        if (_selectedCustomer == null)
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
        _invoice.Attachments.Remove(attachment);
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



