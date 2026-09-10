using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MudBlazor;
using Kuestencode.Core.Interfaces;
using Kuestencode.Core.Models;
using Kuestencode.Shared.UI.Components;
using Kuestencode.Shared.UI.Pages;
using Kuestencode.Werkbank.Host.Services;

namespace Kuestencode.Werkbank.Host.Pages.Customers;

public partial class List
{
    private List<Customer> _customers = new();
    private string _searchString = string.Empty;
    private bool _loading = true;
    private TableSortSync _sort = null!;

    private IEnumerable<Customer> _filteredCustomers => _customers.Where(c =>
        string.IsNullOrWhiteSpace(_searchString) ||
        c.CustomerNumber.Contains(_searchString, StringComparison.OrdinalIgnoreCase) ||
        c.Name.Contains(_searchString, StringComparison.OrdinalIgnoreCase) ||
        (c.Email != null && c.Email.Contains(_searchString, StringComparison.OrdinalIgnoreCase)) ||
        c.City.Contains(_searchString, StringComparison.OrdinalIgnoreCase)
    );

    protected override async Task OnInitializedAsync()
    {
        _sort = new TableSortSync(NavigationManager);
        await LoadCustomers();
    }

    private async Task LoadCustomers()
    {
        _loading = true;
        try
        {
            _customers = await CustomerService.GetAllAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Fehler beim Laden der Kunden: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private void CreateCustomer()
    {
        NavigationManager.NavigateTo("/customers/create");
    }

    private void EditCustomer(int id)
    {
        NavigationManager.NavigateTo($"/customers/edit/{id}");
    }

    private async Task DeleteCustomer(Customer customer)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.ContentText, $"Möchten Sie den Kunden '{customer.Name}' wirklich löschen?" },
            { x => x.ConfirmButtonText, "Löschen" },
            { x => x.ConfirmButtonColor, Color.Error }
        };

        var dialog = await DialogService.ShowAsync<ConfirmDialog>("Kunde löschen", parameters);
        var result = await dialog.Result;

        if (result != null && !result.Canceled)
        {
            try
            {
                await CustomerService.DeleteAsync(customer.Id);
                Snackbar.Add(message: $"Kunde '{customer.Name}' wurde gelöscht.", Severity.Success);
                await LoadCustomers();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Fehler beim Löschen: {ex.Message}", Severity.Error);
            }
        }
    }
}
