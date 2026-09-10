using FluentAssertions;
using Kuestencode.Werkbank.Recepta.Data.Repositories;
using Kuestencode.Werkbank.Recepta.Domain.Entities;
using Kuestencode.Werkbank.Recepta.Domain.Enums;
using Kuestencode.Werkbank.Recepta.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Recepta.Tests.Services;

public class DocumentFileServiceTests
{
    private readonly Mock<IDocumentFileRepository> _fileRepository = new();
    private readonly Mock<IDocumentRepository> _documentRepository = new();
    private readonly DocumentFileService _service;

    public DocumentFileServiceTests()
    {
        var configuration = new ConfigurationBuilder().Build();
        _service = new DocumentFileService(
            _fileRepository.Object,
            _documentRepository.Object,
            NullLogger<DocumentFileService>.Instance,
            configuration);
    }

    private static Document MakeDocument(DocumentStatus status) => new()
    {
        Id = Guid.NewGuid(),
        DocumentNumber = "ER-2026-0001",
        InvoiceNumber = "RE-1",
        Status = status
    };

    [Fact]
    public async Task Upload_GebuchterBeleg_WirftExceptionOhneDateizugriff()
    {
        var doc = MakeDocument(DocumentStatus.Booked);
        _documentRepository.Setup(r => r.GetByIdAsync(doc.Id)).ReturnsAsync(doc);

        var act = () => _service.UploadAsync(doc.Id, new MemoryStream(), "test.pdf", "application/pdf");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Draft*");
    }

    [Fact]
    public async Task Delete_DateiAnGebuchtemBeleg_WirftException()
    {
        var doc = MakeDocument(DocumentStatus.Booked);
        var file = new DocumentFile { Id = Guid.NewGuid(), DocumentId = doc.Id, FileName = "a.pdf", StoragePath = "/tmp/does-not-exist.pdf" };
        _fileRepository.Setup(r => r.GetByIdAsync(file.Id)).ReturnsAsync(file);
        _documentRepository.Setup(r => r.GetByIdAsync(doc.Id)).ReturnsAsync(doc);

        var act = () => _service.DeleteAsync(file.Id);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Draft*");
        _fileRepository.Verify(r => r.DeleteAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Delete_DateiAnEntwurf_LoeschtDatenbankEintrag()
    {
        var doc = MakeDocument(DocumentStatus.Draft);
        var file = new DocumentFile { Id = Guid.NewGuid(), DocumentId = doc.Id, FileName = "a.pdf", StoragePath = "/tmp/does-not-exist.pdf" };
        _fileRepository.Setup(r => r.GetByIdAsync(file.Id)).ReturnsAsync(file);
        _documentRepository.Setup(r => r.GetByIdAsync(doc.Id)).ReturnsAsync(doc);
        _fileRepository.Setup(r => r.DeleteAsync(file.Id)).Returns(Task.CompletedTask);

        await _service.DeleteAsync(file.Id);

        _fileRepository.Verify(r => r.DeleteAsync(file.Id), Times.Once);
    }
}
