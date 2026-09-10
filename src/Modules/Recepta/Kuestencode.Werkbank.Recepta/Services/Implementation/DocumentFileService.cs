using Kuestencode.Werkbank.Recepta.Data.Repositories;
using Kuestencode.Werkbank.Recepta.Domain.Dtos;
using Kuestencode.Werkbank.Recepta.Domain.Entities;

namespace Kuestencode.Werkbank.Recepta.Services;

/// <summary>
/// Service-Implementierung für Dateianhänge.
/// Speicherung: /app/data/{year}/{documentId}/{fileName}
/// </summary>
public class DocumentFileService : IDocumentFileService
{
    private readonly IDocumentFileRepository _fileRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly ILogger<DocumentFileService> _logger;
    private readonly string _storagePath;

    public DocumentFileService(
        IDocumentFileRepository fileRepository,
        IDocumentRepository documentRepository,
        ILogger<DocumentFileService> logger,
        IConfiguration configuration)
    {
        _fileRepository = fileRepository;
        _documentRepository = documentRepository;
        _logger = logger;
        _storagePath = configuration.GetValue<string>("FileStorage:Path") ?? "/app/data";
    }

    public async Task<DocumentFileDto> UploadAsync(Guid documentId, Stream file, string fileName, string contentType)
    {
        var document = await _documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            throw new InvalidOperationException($"Beleg mit ID {documentId} nicht gefunden.");
        }

        if (!document.IsEditable)
        {
            throw new InvalidOperationException(
                $"Dateianhänge können nur bei Belegen im Status 'Draft' geändert werden. Status: {document.Status}");
        }

        // Jahr-basierte Verzeichnisstruktur: /app/data/{year}/{documentId}/
        var year = DateTime.UtcNow.Year.ToString();
        var directory = Path.Combine(_storagePath, year, documentId.ToString());
        Directory.CreateDirectory(directory);

        // Datei mit Original-Dateinamen speichern
        var storagePath = Path.Combine(directory, fileName);

        // Bei Namenskollision: Suffix anhängen
        if (File.Exists(storagePath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var counter = 1;
            do
            {
                storagePath = Path.Combine(directory, $"{nameWithoutExt}_{counter}{extension}");
                counter++;
            } while (File.Exists(storagePath));
        }

        await using var fileStream = File.Create(storagePath);
        await file.CopyToAsync(fileStream);
        var fileSize = fileStream.Length;

        var documentFile = new DocumentFile
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            FileName = fileName,
            ContentType = contentType,
            FileSize = fileSize,
            StoragePath = storagePath
        };

        await _fileRepository.AddAsync(documentFile);
        _logger.LogInformation("Datei '{FileName}' für Beleg {DocumentId} gespeichert unter {Path}",
            fileName, documentId, storagePath);

        return MapToDto(documentFile);
    }

    public async Task<List<DocumentFileDto>> GetByDocumentIdAsync(Guid documentId)
    {
        var files = await _fileRepository.GetByDocumentIdAsync(documentId);
        return files.Select(MapToDto).ToList();
    }

    public async Task<(Stream Content, string FileName, string ContentType)> DownloadAsync(Guid fileId)
    {
        var file = await _fileRepository.GetByIdAsync(fileId);
        if (file == null)
        {
            throw new InvalidOperationException($"Datei mit ID {fileId} nicht gefunden.");
        }

        if (!File.Exists(file.StoragePath))
        {
            throw new InvalidOperationException("Datei nicht auf dem Server gefunden.");
        }

        var stream = File.OpenRead(file.StoragePath);
        return (stream, file.FileName, file.ContentType);
    }

    public async Task DeleteAsync(Guid fileId)
    {
        var file = await _fileRepository.GetByIdAsync(fileId);
        if (file == null)
        {
            throw new InvalidOperationException($"Datei mit ID {fileId} nicht gefunden.");
        }

        var document = await _documentRepository.GetByIdAsync(file.DocumentId);
        if (document != null && !document.IsEditable)
        {
            throw new InvalidOperationException(
                $"Dateianhänge können nur bei Belegen im Status 'Draft' geändert werden. Status: {document.Status}");
        }

        if (File.Exists(file.StoragePath))
        {
            File.Delete(file.StoragePath);
        }

        await _fileRepository.DeleteAsync(fileId);
        _logger.LogInformation("Datei '{FileName}' gelöscht", file.FileName);
    }

    private static DocumentFileDto MapToDto(DocumentFile file)
    {
        return new DocumentFileDto
        {
            Id = file.Id,
            DocumentId = file.DocumentId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileSize = file.FileSize,
            CreatedAt = file.CreatedAt
        };
    }
}
