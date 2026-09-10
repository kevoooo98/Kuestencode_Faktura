using Kuestencode.Werkbank.Recepta.Domain.Dtos;
using Kuestencode.Werkbank.Recepta.Services;
using Kuestencode.Shared.Contracts.Recepta;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.UI.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Kuestencode.Werkbank.Recepta.Controllers;

[ApiController]
[Route("api/recepta")]
[RequireRole(UserRole.Admin, UserRole.Buero)]
public class FilesController : ControllerBase
{
    private readonly IDocumentFileService _fileService;
    private readonly IDocumentService _documentService;
    private readonly IOcrService _ocrService;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        IDocumentFileService fileService,
        IDocumentService documentService,
        IOcrService ocrService,
        ILogger<FilesController> logger)
    {
        _fileService = fileService;
        _documentService = documentService;
        _ocrService = ocrService;
        _logger = logger;
    }

    /// <summary>
    /// Lädt einen Dateianhang hoch und führt optional OCR aus.
    /// </summary>
    [HttpPost("documents/{documentId:guid}/files")]
    public async Task<ActionResult<DocumentFileDto>> Upload(Guid documentId, IFormFile file, [FromQuery] bool ocr = true)
    {
        try
        {
            await using var stream = file.OpenReadStream();
            var documentFile = await _fileService.UploadAsync(documentId, stream, file.FileName, file.ContentType);

            if (ocr)
            {
                try
                {
                    var (downloadStream, _, _) = await _fileService.DownloadAsync(documentFile.Id);
                    await using (downloadStream)
                    {
                        var ocrText = await _ocrService.ExtractTextAsync(downloadStream, file.FileName);

                        if (!string.IsNullOrWhiteSpace(ocrText))
                        {
                            await _documentService.UpdateOcrTextAsync(documentId, ocrText);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OCR für Datei '{FileName}' fehlgeschlagen – Upload war dennoch erfolgreich", file.FileName);
                }
            }

            return Created($"/api/recepta/files/{documentFile.Id}", documentFile);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Lädt einen Dateianhang herunter.
    /// </summary>
    [HttpGet("documents/{documentId:guid}/files")]
    public async Task<ActionResult<List<ReceptaDocumentFileDto>>> GetByDocument(Guid documentId)
    {
        var files = await _fileService.GetByDocumentIdAsync(documentId);
        var result = files.Select(f => new ReceptaDocumentFileDto
        {
            Id = f.Id,
            DocumentId = f.DocumentId,
            FileName = f.FileName,
            ContentType = f.ContentType,
            FileSize = f.FileSize
        }).ToList();

        return Ok(result);
    }

    [HttpGet("files/{fileId:guid}")]
    public async Task<IActionResult> Download(Guid fileId, [FromQuery] bool inline = false)
    {
        try
        {
            var (stream, fileName, contentType) = await _fileService.DownloadAsync(fileId);

            if (inline)
            {
                var contentDisposition = new ContentDispositionHeaderValue("inline");
                contentDisposition.SetHttpFileName(fileName);
                Response.Headers.ContentDisposition = contentDisposition.ToString();
                return File(stream, contentType);
            }

            return File(stream, contentType, fileName);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Löscht einen Dateianhang.
    /// </summary>
    [HttpDelete("files/{fileId:guid}")]
    public async Task<IActionResult> Delete(Guid fileId)
    {
        try
        {
            await _fileService.DeleteAsync(fileId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
