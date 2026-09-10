using Kuestencode.Core.Interfaces;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.UI.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Kuestencode.Rapport.Controllers;

[ApiController]
[Route("api/rapport/customers")]
[RequireRole(UserRole.Admin, UserRole.Buero, UserRole.Mitarbeiter)]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet]
    public async Task<ActionResult<List<CustomerSelectDto>>> GetAll()
    {
        var customers = await _customerService.GetAllAsync();
        var result = customers
            .OrderBy(c => c.Name)
            .Select(c => new CustomerSelectDto
            {
                Id = c.Id,
                Name = c.Name,
                CustomerNumber = c.CustomerNumber
            })
            .ToList();

        return Ok(result);
    }
}

public class CustomerSelectDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CustomerNumber { get; set; }
}
