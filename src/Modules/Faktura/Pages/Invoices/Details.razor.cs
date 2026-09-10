using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace Kuestencode.Faktura.Pages.Invoices;

public partial class Details
{
    [Parameter]
    public int Id { get; set; }

    private Invoice? _invoice;
    private List<Kuestencode.Shared.Contracts.Faktura.AuditLogEntryDto> _auditLog = new();
    private Company? _company;
    private bool _loading = true;
    private string? _fromFilter;
    private string? _fromSearch;
    private bool _downloading = false;
    private bool _isEmailConfigured = false;
    private System.Globalization.CultureInfo _culture = new System.Globalization.CultureInfo("de-DE");

    // Zahlung erfassen Modal
    private bool _zahlungModalOpen = false;
    private decimal _zahlungBetrag;
    private DateTime? _zahlungDatum = DateTime.Today;
    private string? _zahlungNotiz;
    private bool _zahlungSaving = false;

    protected override async Task OnInitializedAsync()
    {
        var queryParams = QueryHelpers.ParseQuery(new Uri(NavigationManager.Uri).Query);
        if (queryParams.TryGetValue("from", out var from)) _fromFilter = from.ToString();
        if (queryParams.TryGetValue("fromsearch", out var fromsearch)) _fromSearch = fromsearch.ToString();

        await LoadInvoice();
        await LoadCompany();
        await CheckEmailConfiguration();
        await CheckForSendAction();
    }

    private async Task CheckForSendAction()
    {
        var uri = new Uri(NavigationManager.Uri);
        var queryParams = QueryHelpers.ParseQuery(uri.Query);

        if (queryParams.TryGetValue("action", out var action) && action == "send")
        {
            // Navigiere zu bereinigter URL
            NavigationManager.NavigateTo($"/faktura/invoices/details/{Id}", replace: true);

            // Warte kurz damit die Navigation abgeschlossen ist
            await Task.Delay(100);

            // Öffne den E-Mail-Dialog
            if (_invoice != null && _isEmailConfigured)
            {
                await OpenEmailDialog();
            }
            else if (!_isEmailConfigured)
            {
                Snackbar.Add("E-Mail-Versand ist nicht konfiguriert.", Severity.Warning);
            }
        }
    }

    private async Task CheckEmailConfiguration()
    {
        try
        {
            _isEmailConfigured = await CompanyService.IsEmailConfiguredAsync();
        }
        catch
        {
            _isEmailConfigured = false;
        }
    }

    private async Task LoadCompany()
    {
        try
        {
            _company = await CompanyService.GetCompanyAsync();
        }
        catch
        {
            // Fehler ignorieren, falls Firmendaten noch nicht angelegt
        }
    }

    private async Task LoadInvoice()
    {
        _loading = true;
        try
        {
            _invoice = await InvoiceService.GetByIdAsync(Id, includeCustomer: true, includeItems: true);
            await LoadProjectNameAsync();
            _auditLog = await InvoiceService.GetAuditLogAsync(Id);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Laden der Rechnung: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadProjectNameAsync()
    {
        if (_invoice?.ProjectId == null)
        {
            return;
        }

        try
        {
            var project = await ActaApiClient.GetProjectByExternalIdAsync(_invoice.ProjectId.Value);
            _invoice.ProjectName = project?.Name;
        }
        catch
        {
            // Acta-Modul evtl. nicht verfügbar - Projektname bleibt leer
        }
    }

    private bool _markAsPaidOpen = false;
    private DateTime? _paidDateInput = DateTime.Today;

    private void OpenMarkAsPaidDialog()
    {
        _paidDateInput = DateTime.Today;
        _markAsPaidOpen = true;
    }

    private void CloseMarkAsPaidDialog()
    {
        _markAsPaidOpen = false;
    }

    private async Task ConfirmMarkAsPaid()
    {
        if (_invoice == null) return;

        var paidDate = (_paidDateInput ?? DateTime.Today).Date;
        _markAsPaidOpen = false;

        try
        {
            await InvoiceService.MarkAsPaidAsync(_invoice.Id, paidDate);
            Snackbar.Add($"Faktura {_invoice.InvoiceNumber} wurde als beglichen markiert.", Severity.Success);
            await LoadInvoice();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler: {ex.Message}", Severity.Error);
        }
    }

    private async Task CreateCreditNote()
    {
        if (_invoice == null) return;
        try
        {
            var creditNote = await InvoiceService.CreateCreditNoteFromInvoiceAsync(_invoice.Id, DateTime.Today);
            NavigationManager.NavigateTo($"/faktura/credit-notes/edit/{creditNote.Id}");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Erstellen der Gutschrift: {ex.Message}", Severity.Error);
        }
    }

    private async Task CancelInvoice()
    {
        if (_invoice == null) return;

        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.ContentText, $"Rechnung {_invoice.InvoiceNumber} wirklich stornieren? Die Rechnung bleibt als storniert erhalten und kann danach nicht mehr geändert werden." },
            { x => x.ButtonText, "Stornieren" },
            { x => x.Color, Color.Error }
        };

        var dialog = await DialogService.ShowAsync<ConfirmDialog>("Rechnung stornieren", parameters);
        var result = await dialog.Result;

        if (result is null || result.Canceled) return;

        try
        {
            await InvoiceService.CancelAsync(_invoice.Id, null);
            Snackbar.Add($"Rechnung {_invoice.InvoiceNumber} wurde storniert", Severity.Success);
            await LoadInvoice();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Stornieren: {ex.Message}", Severity.Error);
        }
    }

    private Color GetStatusColor(InvoiceStatus status)
    {
        return status switch
        {
            InvoiceStatus.Draft => Color.Default,
            InvoiceStatus.Sent => Color.Info,
            InvoiceStatus.Paid => Color.Success,
            InvoiceStatus.Overdue => Color.Error,
            InvoiceStatus.Cancelled => Color.Dark,
            InvoiceStatus.PartiallyPaid => Color.Warning,
            _ => Color.Default
        };
    }

    private string GetStatusText(InvoiceStatus status)
    {
        return status switch
        {
            InvoiceStatus.Draft => "Entwurf",
            InvoiceStatus.Sent => "Versendet",
            InvoiceStatus.Paid => "Beglichen",
            InvoiceStatus.Overdue => "Überfällig",
            InvoiceStatus.Cancelled => "Storniert",
            InvoiceStatus.PartiallyPaid => "Teilgezahlt",
            _ => status.ToString()
        };
    }

    private void OpenZahlungModal()
    {
        if (_invoice == null) return;
        _zahlungBetrag = _invoice.RemainingAmount > 0 ? _invoice.RemainingAmount : 0;
        _zahlungDatum = DateTime.Today;
        _zahlungNotiz = null;
        _zahlungModalOpen = true;
    }

    private void CloseZahlungModal()
    {
        _zahlungModalOpen = false;
    }

    private async Task ErfasseZahlung()
    {
        if (_invoice == null || _zahlungBetrag <= 0) return;

        _zahlungSaving = true;
        try
        {
            await PaymentService.ZahlungErfassenAsync(
                _invoice.Id,
                _zahlungBetrag,
                _zahlungDatum ?? DateTime.Today,
                _zahlungNotiz);

            _zahlungModalOpen = false;
            Snackbar.Add($"Zahlung über {_zahlungBetrag.ToString("C2", _culture)} wurde erfasst.", Severity.Success);
            await LoadInvoice();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Erfassen der Zahlung: {ex.Message}", Severity.Error);
        }
        finally
        {
            _zahlungSaving = false;
        }
    }

    private async Task LoescheZahlung(int zahlungId)
    {
        if (_invoice == null) return;
        try
        {
            await PaymentService.ZahlungLoeschenAsync(zahlungId);
            Snackbar.Add("Zahlung wurde gelöscht.", Severity.Success);
            await LoadInvoice();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Löschen der Zahlung: {ex.Message}", Severity.Error);
        }
    }

    private async Task DownloadPdf()
    {
        if (_invoice == null) return;

        _downloading = true;
        try
        {
            var pdfBytes = await PdfGeneratorService.GenerateInvoicePdfAsync(_invoice.Id);
            var fileName = $"{_invoice.InvoiceNumber}.pdf";

            await JSRuntime.InvokeVoidAsync("downloadFile", fileName, Convert.ToBase64String(pdfBytes));
            Snackbar.Add($"PDF für Rechnung {_invoice.InvoiceNumber} wurde heruntergeladen.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Generieren des PDFs: {ex.Message}", Severity.Error);
        }
        finally
        {
            _downloading = false;
        }
    }

    private async Task DownloadZugferdPdf()
    {
        if (_invoice == null) return;

        _downloading = true;
        try
        {
            // Validierung
            var (isValid, missingFields) = await XRechnungService.ValidateForXRechnungAsync(_invoice.Id);
            if (!isValid)
            {
                Snackbar.Add($"XRechnung erfordert vollständige Firmen- und Kundendaten. Fehlend: {string.Join(", ", missingFields)}", Severity.Warning);
                return;
            }

            var pdfBytes = await XRechnungService.GenerateZugferdPdfAsync(_invoice.Id);
            var fileName = $"{_invoice.InvoiceNumber}_zugferd.pdf";

            await JSRuntime.InvokeVoidAsync("downloadFile", fileName, Convert.ToBase64String(pdfBytes));
            Snackbar.Add($"ZUGFeRD-PDF für Rechnung {_invoice.InvoiceNumber} wurde heruntergeladen.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Generieren des ZUGFeRD-PDFs: {ex.Message}", Severity.Error);
        }
        finally
        {
            _downloading = false;
        }
    }

    private async Task DownloadXRechnung()
    {
        if (_invoice == null) return;

        _downloading = true;
        try
        {
            // Validierung
            var (isValid, missingFields) = await XRechnungService.ValidateForXRechnungAsync(_invoice.Id);
            if (!isValid)
            {
                Snackbar.Add($"XRechnung erfordert vollständige Firmen- und Kundendaten. Fehlend: {string.Join(", ", missingFields)}", Severity.Warning);
                return;
            }

            var xmlContent = await XRechnungService.GenerateXRechnungXmlAsync(_invoice.Id);
            var fileName = $"{_invoice.InvoiceNumber}_xrechnung.xml";

            // Convert XML string to base64
            var xmlBytes = System.Text.Encoding.UTF8.GetBytes(xmlContent);
            await JSRuntime.InvokeVoidAsync("downloadFile", fileName, Convert.ToBase64String(xmlBytes));

            Snackbar.Add($"XRechnung (XML) für Rechnung {_invoice.InvoiceNumber} wurde heruntergeladen.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Generieren der XRechnung: {ex.Message}", Severity.Error);
        }
        finally
        {
            _downloading = false;
        }
    }

    private async Task OpenEmailDialog()
    {
        if (_invoice == null) return;

        var parameters = new DialogParameters<SendEmailDialog>
        {
            { x => x.Invoice, _invoice },
            { x => x.CustomerEmail, _invoice.Customer?.Email },
            { x => x.OnSend, EventCallback.Factory.Create<(string Email, string? Message, EmailAttachmentFormat Format, string? CcEmails, string? BccEmails, bool IncludeClosing)>(this, SendInvoiceEmail) }
        };

        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await DialogService.ShowAsync<SendEmailDialog>("E-Mail versenden", parameters, options);
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
                await LoadInvoice(); // Reload to update email tracking info
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Versenden der E-Mail: {ex.Message}", Severity.Error);
            throw; // Re-throw to prevent dialog from closing
        }
    }

    private async Task PrintAndConfirm()
    {
        if (_invoice == null) return;

        try
        {
            await JSRuntime.InvokeVoidAsync("openInNewTab", $"api/invoice/{_invoice.Id}/pdf-print");

            var parameters = new DialogParameters();
            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small };
            var dialog = await DialogService.ShowAsync<PrintConfirmationDialog>("Rechnung gedruckt?", parameters, options);
            var result = await dialog.Result;

            if (result is not null && !result.Canceled)
            {
                await InvoiceService.MarkAsPrintedAsync(_invoice.Id);
                Snackbar.Add("Rechnung als gedruckt markiert", Severity.Success);
                await LoadInvoice();
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler: {ex.Message}", Severity.Error);
        }
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

    private record ItemSection(InvoiceItem? Header, List<InvoiceItem> Items);

    private List<ItemSection> GetItemSections()
    {
        if (_invoice == null) return [];
        var result = new List<ItemSection>();
        InvoiceItem? currentHeader = null;
        var currentItems = new List<InvoiceItem>();

        foreach (var item in _invoice.Items.OrderBy(i => i.Position))
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
}
