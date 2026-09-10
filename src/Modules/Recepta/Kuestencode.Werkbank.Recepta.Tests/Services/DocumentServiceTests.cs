using FluentAssertions;
using Kuestencode.Werkbank.Recepta.Data;
using Kuestencode.Werkbank.Recepta.Data.Repositories;
using Kuestencode.Werkbank.Recepta.Domain.Entities;
using Kuestencode.Werkbank.Recepta.Domain.Enums;
using Kuestencode.Werkbank.Recepta.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Recepta.Tests.Services;

public class DocumentServiceTests
{
    private readonly Mock<IDocumentRepository> _documentRepository = new();
    private readonly Mock<IDocumentAllocationRepository> _allocationRepository = new();
    private readonly Mock<ISupplierRepository> _supplierRepository = new();
    private readonly Mock<IOcrService> _ocrService = new();
    private readonly Mock<IOcrPatternService> _patternService = new();
    private readonly Mock<IXRechnungService> _xRechnungService = new();
    private readonly Mock<IDocumentFileService> _fileService = new();
    private readonly ReceptaDbContext _context;
    private readonly DocumentService _service;

    public DocumentServiceTests()
    {
        var options = new DbContextOptionsBuilder<ReceptaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ReceptaDbContext(options);
        _service = new DocumentService(
            _documentRepository.Object,
            _allocationRepository.Object,
            _supplierRepository.Object,
            _ocrService.Object,
            _patternService.Object,
            _xRechnungService.Object,
            _fileService.Object,
            _context,
            NullLogger<DocumentService>.Instance);
    }

    private static Document MakeDocument(DocumentStatus status = DocumentStatus.Draft) => new()
    {
        Id = Guid.NewGuid(),
        DocumentNumber = "ER-2026-0001",
        InvoiceNumber = "RE-1",
        Status = status
    };

    // ─── ChangeStatusAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeStatus_BookedZurueckZuDraft_WirftException()
    {
        var doc = MakeDocument(DocumentStatus.Booked);
        _documentRepository.Setup(r => r.GetByIdAsync(doc.Id)).ReturnsAsync(doc);

        var act = () => _service.ChangeStatusAsync(doc.Id, DocumentStatus.Draft);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Ungültiger Statusübergang*");
        _documentRepository.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Fact]
    public async Task ChangeStatus_DraftZuBooked_Funktioniert()
    {
        var doc = MakeDocument(DocumentStatus.Draft);
        _documentRepository.Setup(r => r.GetByIdAsync(doc.Id)).ReturnsAsync(doc);
        _documentRepository.Setup(r => r.UpdateAsync(doc)).Returns(Task.CompletedTask);

        await _service.ChangeStatusAsync(doc.Id, DocumentStatus.Booked);

        doc.Status.Should().Be(DocumentStatus.Booked);
    }

    // ─── UpdateOcrTextAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateOcrText_GebuchterBeleg_WirftException()
    {
        var doc = MakeDocument(DocumentStatus.Booked);
        _documentRepository.Setup(r => r.GetByIdAsync(doc.Id)).ReturnsAsync(doc);

        var act = () => _service.UpdateOcrTextAsync(doc.Id, "neuer text");

        await act.Should().ThrowAsync<InvalidOperationException>();
        _documentRepository.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Fact]
    public async Task UpdateOcrText_EntwurfsBeleg_AktualisiertText()
    {
        var doc = MakeDocument(DocumentStatus.Draft);
        _documentRepository.Setup(r => r.GetByIdAsync(doc.Id)).ReturnsAsync(doc);
        _documentRepository.Setup(r => r.UpdateAsync(doc)).Returns(Task.CompletedTask);

        await _service.UpdateOcrTextAsync(doc.Id, "neuer text");

        doc.OcrRawText.Should().Be("neuer text");
    }

    // ─── DeleteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_GebuchterBeleg_WirftExceptionUndRuehrtKeineDatei()
    {
        var doc = MakeDocument(DocumentStatus.Booked);
        doc.Files.Add(new DocumentFile { Id = Guid.NewGuid(), DocumentId = doc.Id, FileName = "a.pdf" });
        _documentRepository.Setup(r => r.GetByIdAsync(doc.Id)).ReturnsAsync(doc);

        var act = () => _service.DeleteAsync(doc.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _fileService.Verify(f => f.DeleteAsync(It.IsAny<Guid>()), Times.Never);
        _documentRepository.Verify(r => r.DeleteAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Delete_EntwurfMitDateien_LoeschtDateienVorDemBeleg()
    {
        var doc = MakeDocument(DocumentStatus.Draft);
        var fileId = Guid.NewGuid();
        doc.Files.Add(new DocumentFile { Id = fileId, DocumentId = doc.Id, FileName = "a.pdf" });
        _documentRepository.Setup(r => r.GetByIdAsync(doc.Id)).ReturnsAsync(doc);
        _documentRepository.Setup(r => r.DeleteAsync(doc.Id)).Returns(Task.CompletedTask);

        await _service.DeleteAsync(doc.Id);

        _fileService.Verify(f => f.DeleteAsync(fileId), Times.Once);
        _documentRepository.Verify(r => r.DeleteAsync(doc.Id), Times.Once);
    }

    // ─── GetAuditLogAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditLog_LiefertNurEintraegeDesAngefragtenBelegs_NeuesteZuerst()
    {
        var docId = Guid.NewGuid().ToString();
        var otherId = Guid.NewGuid().ToString();
        _context.AuditLogEntries.AddRange(
            new AuditLogEntry { Id = Guid.NewGuid(), EntityName = "Document", EntityId = docId, Action = "Modified", FieldName = "Status", ChangedByUserName = "Alice", ChangedAt = DateTime.UtcNow.AddMinutes(-5) },
            new AuditLogEntry { Id = Guid.NewGuid(), EntityName = "Document", EntityId = docId, Action = "Modified", FieldName = "Notes", ChangedByUserName = "Bob", ChangedAt = DateTime.UtcNow },
            new AuditLogEntry { Id = Guid.NewGuid(), EntityName = "Document", EntityId = otherId, Action = "Created", ChangedByUserName = "Carol", ChangedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var result = await _service.GetAuditLogAsync(Guid.Parse(docId));

        result.Should().HaveCount(2);
        result[0].FieldName.Should().Be("Notes");
        result[1].FieldName.Should().Be("Status");
    }
}
