using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Acta;
using Kuestencode.Shared.Contracts.Faktura;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.Contracts.Navigation;
using Kuestencode.Shared.Contracts.Rapport;
using Kuestencode.Shared.Contracts.Recepta;

namespace Kuestencode.Faktura.Tests.TestDoubles;

/// <summary>
/// Testdouble für den Host-API-Client: liefert feste Kundendaten statt echter HTTP-Aufrufe.
/// </summary>
public class FakeHostApiClient : IHostApiClient
{
    public Task<CompanyDto?> GetCompanyAsync() => Task.FromResult<CompanyDto?>(null);

    public Task UpdateCompanyAsync(UpdateCompanyRequest request) => Task.CompletedTask;

    public Task<NumberFormatSettingsDto?> GetNumberFormatSettingsAsync() => Task.FromResult<NumberFormatSettingsDto?>(null);

    public Task UpdateNumberFormatSettingsAsync(UpdateNumberFormatSettingsRequest request) => Task.CompletedTask;

    public Task<CustomerDto?> GetCustomerAsync(int customerId) => Task.FromResult<CustomerDto?>(new CustomerDto
    {
        Id = customerId,
        CustomerNumber = $"K{customerId:D5}",
        Name = "Nordlicht Media",
        Address = "Hafenstraße 1",
        PostalCode = "20457",
        City = "Hamburg",
        Country = "Deutschland"
    });

    public Task<List<CustomerDto>> GetAllCustomersAsync() => Task.FromResult(new List<CustomerDto>());

    public Task<List<NavItemDto>> GetNavigationAsync() => Task.FromResult(new List<NavItemDto>());

    public Task<List<TeamMemberDto>> GetTeamMembersAsync() => Task.FromResult(new List<TeamMemberDto>());

    public Task<TeamMemberDto?> GetTeamMemberAsync(Guid id) => Task.FromResult<TeamMemberDto?>(null);

    public Task<ProjectHoursResponseDto?> GetProjectHoursAsync(int projectId) => Task.FromResult<ProjectHoursResponseDto?>(null);

    public Task<List<ActaProjectDto>> GetActaProjectsAsync() => Task.FromResult(new List<ActaProjectDto>());

    public Task<ActaProjectDto?> GetActaProjectAsync(int externalId) => Task.FromResult<ActaProjectDto?>(null);

    public Task<ProjectExpensesResponseDto?> GetProjectExpensesAsync(Guid projectId) => Task.FromResult<ProjectExpensesResponseDto?>(null);

    public Task<ProjectInvoicesResponseDto?> GetProjectInvoicesAsync(int projectId) => Task.FromResult<ProjectInvoicesResponseDto?>(null);

    public Task<string> GenerateCustomerNumberAsync() => Task.FromResult("K00001");

    public Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request) => Task.FromResult(new CustomerDto
    {
        Name = request.Name
    });

    public Task<List<MitarbeiterRolleDto>> GetMitarbeiterRollenAsync() => Task.FromResult(new List<MitarbeiterRolleDto>());

    public Task<bool> SendEmailAsync(SendEmailRequest request) => Task.FromResult(true);

    public Task<(bool Success, string? ErrorMessage)> TestEmailConnectionAsync() => Task.FromResult<(bool, string?)>((true, null));
}
