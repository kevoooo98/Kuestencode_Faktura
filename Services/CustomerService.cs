using InvoiceApp.Data.Repositories;
using InvoiceApp.Models;

namespace InvoiceApp.Services;

public interface ICustomerService
{
    Task<List<Customer>> GetAllAsync();
    Task<Customer?> GetByIdAsync(int id);
    Task<Customer> CreateAsync(Customer customer);
    Task UpdateAsync(Customer customer);
    Task DeleteAsync(int id);
    Task<string> GenerateCustomerNumberAsync();
    Task<bool> CustomerNumberExistsAsync(string number);
}

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(ICustomerRepository customerRepository, ILogger<CustomerService> logger)
    {
        _customerRepository = customerRepository;
        _logger = logger;
    }

    public async Task<List<Customer>> GetAllAsync()
    {
        try
        {
            var customers = await _customerRepository.GetAllAsync();
            return customers.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen aller Kunden");
            throw;
        }
    }

    public async Task<Customer?> GetByIdAsync(int id)
    {
        try
        {
            return await _customerRepository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Abrufen des Kunden mit ID {CustomerId}", id);
            throw;
        }
    }

    public async Task<Customer> CreateAsync(Customer customer)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(customer.CustomerNumber))
            {
                customer.CustomerNumber = await GenerateCustomerNumberAsync();
            }

            var exists = await CustomerNumberExistsAsync(customer.CustomerNumber);
            if (exists)
            {
                throw new InvalidOperationException($"Kundennummer {customer.CustomerNumber} existiert bereits.");
            }

            return await _customerRepository.AddAsync(customer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Erstellen des Kunden");
            throw;
        }
    }

    public async Task UpdateAsync(Customer customer)
    {
        try
        {
            var existingCustomer = await _customerRepository.GetByIdAsync(customer.Id);
            if (existingCustomer == null)
            {
                throw new InvalidOperationException($"Kunde mit ID {customer.Id} wurde nicht gefunden.");
            }

            // Check if customer number was changed and if new number already exists
            if (existingCustomer.CustomerNumber != customer.CustomerNumber)
            {
                var exists = await CustomerNumberExistsAsync(customer.CustomerNumber);
                if (exists)
                {
                    throw new InvalidOperationException($"Kundennummer {customer.CustomerNumber} existiert bereits.");
                }
            }

            await _customerRepository.UpdateAsync(customer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Aktualisieren des Kunden mit ID {CustomerId}", customer.Id);
            throw;
        }
    }

    public async Task DeleteAsync(int id)
    {
        try
        {
            var customer = await _customerRepository.GetByIdAsync(id);
            if (customer == null)
            {
                throw new InvalidOperationException($"Kunde mit ID {id} wurde nicht gefunden.");
            }

            await _customerRepository.DeleteAsync(customer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Löschen des Kunden mit ID {CustomerId}", id);
            throw;
        }
    }

    public async Task<string> GenerateCustomerNumberAsync()
    {
        try
        {
            return await _customerRepository.GenerateCustomerNumberAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Generieren der Kundennummer");
            throw;
        }
    }

    public async Task<bool> CustomerNumberExistsAsync(string number)
    {
        try
        {
            return await _customerRepository.CustomerNumberExistsAsync(number);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Prüfen der Kundennummer {CustomerNumber}", number);
            throw;
        }
    }
}
