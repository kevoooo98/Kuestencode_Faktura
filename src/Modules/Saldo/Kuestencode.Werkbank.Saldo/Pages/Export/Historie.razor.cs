using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using Kuestencode.Core.Auth;
using Kuestencode.Werkbank.Saldo.Domain.Dtos;
using Kuestencode.Werkbank.Saldo.Services;
using Kuestencode.Shared.UI.Pages;

namespace Kuestencode.Werkbank.Saldo.Pages.Export;

public partial class Historie
{
    [Inject] private IDatevExportService ExportService { get; set; } = default!;
    [Inject] private ICurrentUserAccessor CurrentUserAccessor { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    private bool _loading = true;
    private string? _exportingId;

    private List<ExportLogDto> _historie = new();

    private DateTime? _vonDate = new DateTime(DateTime.Today.Year, 1, 1);
    private DateTime? _bisDate = new DateTime(DateTime.Today.Year, 12, 31);
    private TableSortSync _sort = null!;

    protected override async Task OnInitializedAsync()
    {
        _sort = new TableSortSync(NavigationManager);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _historie = await ExportService.GetExportHistorieAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Laden: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ExportDatevAsync(DateOnly von, DateOnly bis, string id = "neu-datev")
    {
        _exportingId = id;
        StateHasChanged();
        try
        {
            var bytes    = await ExportService.ExportBuchungsstapelAsync(von, bis, CurrentUserAccessor.Get().UserId);
            var fileName = $"EXTF_Buchungsstapel_{von:yyyy}_{GetQuartal(von, bis)}.csv";
            await DownloadAsync(bytes, fileName, "text/csv");
            Snackbar.Add($"DATEV-Export erstellt: {fileName}", Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler: {ex.Message}", Severity.Error);
        }
        finally
        {
            _exportingId = null;
        }
    }

    private async Task ExportBelegeAsync(DateOnly von, DateOnly bis, string id = "neu-belege")
    {
        _exportingId = id;
        StateHasChanged();
        try
        {
            var bytes    = await ExportService.ExportBelegeAsync(von, bis, CurrentUserAccessor.Get().UserId);
            var fileName = $"Belege_{von:yyyy}_{GetQuartal(von, bis)}.zip";
            await DownloadAsync(bytes, fileName, "application/zip");
            Snackbar.Add($"Belege-Export erstellt: {fileName}", Severity.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler: {ex.Message}", Severity.Error);
        }
        finally
        {
            _exportingId = null;
        }
    }

    private DateOnly GetVon() => _vonDate.HasValue
        ? DateOnly.FromDateTime(_vonDate.Value)
        : new DateOnly(DateTime.Today.Year, 1, 1);

    private DateOnly GetBis() => _bisDate.HasValue
        ? DateOnly.FromDateTime(_bisDate.Value)
        : new DateOnly(DateTime.Today.Year, 12, 31);

    private async Task DownloadAsync(byte[] data, string fileName, string contentType)
    {
        var base64 = Convert.ToBase64String(data);
        await JS.InvokeVoidAsync("downloadFileFromBase64", fileName, contentType, base64);
    }

    private static string GetQuartal(DateOnly von, DateOnly bis)
    {
        if (von.Month == 1 && von.Day == 1 && bis.Month == 12 && bis.Day == 31)
            return "FJ";
        return von.Month switch
        {
            1 => "Q1", 4 => "Q2", 7 => "Q3", 10 => "Q4",
            _ => $"{von:MMdd}_{bis:MMdd}"
        };
    }

    private static string GetZeitraumLabel(DateOnly von, DateOnly bis)
    {
        if (von.Month == 1 && von.Day == 1 && bis.Month == 12 && bis.Day == 31)
            return $"Gesamtjahr {von.Year}";
        if (bis.Month - von.Month == 2 && von.Day == 1)
            return $"Q{(von.Month - 1) / 3 + 1} {von.Year}";
        return string.Empty;
    }

    private static string FormatDateigroesse(long bytes) => bytes switch
    {
        < 1024        => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:N1} KB",
        _             => $"{bytes / (1024.0 * 1024):N1} MB"
    };

    private static string GetDatevTooltip(DateOnly von, DateOnly bis)
        => "DATEV-Export für " + von.ToString("dd.MM.yy") + "–" + bis.ToString("dd.MM.yy") + " erneut erstellen";

    private static string GetBelegeTooltip(DateOnly von, DateOnly bis)
        => "Belege-ZIP für " + von.ToString("dd.MM.yy") + "–" + bis.ToString("dd.MM.yy") + " erneut erstellen";
}
