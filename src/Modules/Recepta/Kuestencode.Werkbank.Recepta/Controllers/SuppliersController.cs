using Kuestencode.Werkbank.Recepta.Controllers.Dtos;
using Kuestencode.Werkbank.Recepta.Domain.Dtos;
using Kuestencode.Werkbank.Recepta.Domain.Enums;
using Kuestencode.Werkbank.Recepta.Services;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.UI.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Kuestencode.Werkbank.Recepta.Controllers;

[ApiController]
[Route("api/recepta/suppliers")]
[RequireRole(UserRole.Admin, UserRole.Buero)]
public class SuppliersController : ControllerBase
{
    private readonly ISupplierService _supplierService;
    private readonly ILogger<SuppliersController> _logger;

    public SuppliersController(ISupplierService supplierService, ILogger<SuppliersController> logger)
    {
        _supplierService = supplierService;
        _logger = logger;
    }

    /// <summary>
    /// Lädt alle Lieferanten mit optionaler Suche.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SupplierDto>>> GetAll([FromQuery] string? search = null)
    {
        var suppliers = await _supplierService.GetAllAsync(search);
        return Ok(suppliers);
    }

    /// <summary>
    /// Lädt einen Lieferanten anhand der ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> GetById(Guid id)
    {
        var supplier = await _supplierService.GetByIdAsync(id);
        if (supplier == null)
        {
            return NotFound();
        }

        return Ok(supplier);
    }

    /// <summary>
    /// Erstellt einen neuen Lieferanten.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create([FromBody] CreateSupplierRequest request)
    {
        try
        {
            var dto = new CreateSupplierDto
            {
                SupplierNumber = request.SupplierNumber,
                Name = request.Name,
                Address = request.Address,
                PostalCode = request.PostalCode,
                City = request.City,
                Country = request.Country,
                Email = request.Email,
                Phone = request.Phone,
                TaxId = request.TaxId,
                Iban = request.Iban,
                Bic = request.Bic,
                Notes = request.Notes,
                DefaultCategory = Enum.TryParse<DocumentCategory>(request.DefaultCategory, out var createCat) ? createCat : null
            };

            var supplier = await _supplierService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, supplier);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Aktualisiert einen Lieferanten.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, [FromBody] UpdateSupplierRequest request)
    {
        try
        {
            var dto = new UpdateSupplierDto
            {
                Name = request.Name,
                Address = request.Address,
                PostalCode = request.PostalCode,
                City = request.City,
                Country = request.Country,
                Email = request.Email,
                Phone = request.Phone,
                TaxId = request.TaxId,
                Iban = request.Iban,
                Bic = request.Bic,
                Notes = request.Notes,
                DefaultCategory = Enum.TryParse<DocumentCategory>(request.DefaultCategory, out var updateCat) ? updateCat : null
            };

            var supplier = await _supplierService.UpdateAsync(id, dto);
            return Ok(supplier);
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
    /// Löscht einen Lieferanten (nur wenn keine Belege verknüpft).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _supplierService.DeleteAsync(id);
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
    /// Sucht einen Lieferanten anhand des Namens (für OCR-Vorschlag).
    /// </summary>
    [HttpGet("find-by-name")]
    public async Task<ActionResult<SupplierDto>> FindByName([FromQuery] string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { error = "Name ist erforderlich." });
        }

        var supplier = await _supplierService.FindByNameAsync(name);
        if (supplier == null)
        {
            return NotFound();
        }

        return Ok(supplier);
    }

    /// <summary>
    /// Generiert die nächste verfügbare Lieferantennummer.
    /// </summary>
    [HttpGet("next-number")]
    public async Task<ActionResult<string>> GetNextNumber()
    {
        var number = await _supplierService.GenerateSupplierNumberAsync();
        return Ok(new { number });
    }
}
