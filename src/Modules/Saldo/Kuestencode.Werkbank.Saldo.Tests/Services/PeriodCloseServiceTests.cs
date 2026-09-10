using FluentAssertions;
using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Kuestencode.Werkbank.Saldo.Services;
using Moq;
using Xunit;

namespace Kuestencode.Werkbank.Saldo.Tests.Services;

public class PeriodCloseServiceTests
{
    private readonly Mock<IPeriodCloseRepository> _repository = new();

    private PeriodCloseService CreateService() => new(_repository.Object);

    private static readonly DateOnly Von = new(2026, 1, 1);
    private static readonly DateOnly Bis = new(2026, 3, 31);

    [Fact]
    public async Task ClosePeriodAsync_LegtNeuenAbschlussAn()
    {
        var userId = Guid.NewGuid();
        var exportLogId = Guid.NewGuid();
        _repository.Setup(r => r.AddAsync(It.IsAny<PeriodClose>()))
            .ReturnsAsync((PeriodClose p) => p);

        var service = CreateService();
        var result = await service.ClosePeriodAsync(Von, Bis, userId, exportLogId);

        result.ZeitraumVon.Should().Be(Von);
        result.ZeitraumBis.Should().Be(Bis);
        result.ClosedByUserId.Should().Be(userId);
        result.ExportLogId.Should().Be(exportLogId);
        _repository.Verify(r => r.AddAsync(It.Is<PeriodClose>(p =>
            p.ZeitraumVon == Von && p.ZeitraumBis == Bis && p.ClosedByUserId == userId && p.ExportLogId == exportLogId)), Times.Once);
    }

    [Fact]
    public async Task ClosePeriodAsync_OhneExportLogId_FunktioniertTrotzdem()
    {
        var userId = Guid.NewGuid();
        _repository.Setup(r => r.AddAsync(It.IsAny<PeriodClose>()))
            .ReturnsAsync((PeriodClose p) => p);

        var service = CreateService();
        var result = await service.ClosePeriodAsync(Von, Bis, userId, null);

        result.ExportLogId.Should().BeNull();
    }

    [Fact]
    public async Task GetForRangeAsync_KeinAbschluss_GibtNullZurueck()
    {
        _repository.Setup(r => r.GetForRangeAsync(Von, Bis)).ReturnsAsync((PeriodClose?)null);

        var service = CreateService();
        var result = await service.GetForRangeAsync(Von, Bis);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetForRangeAsync_VorhandenerAbschluss_MapptKorrektAufDto()
    {
        var closedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        _repository.Setup(r => r.GetForRangeAsync(Von, Bis)).ReturnsAsync(new PeriodClose
        {
            Id = Guid.NewGuid(),
            ZeitraumVon = Von,
            ZeitraumBis = Bis,
            ClosedAt = closedAt,
            ClosedByUserId = Guid.NewGuid()
        });

        var service = CreateService();
        var result = await service.GetForRangeAsync(Von, Bis);

        result!.ClosedAt.Should().Be(closedAt);
    }

    [Fact]
    public async Task GetAllAsync_MapptAlleEintraegeAufDtos()
    {
        _repository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<PeriodClose>
        {
            new() { Id = Guid.NewGuid(), ZeitraumVon = Von, ZeitraumBis = Bis, ClosedByUserId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), ZeitraumVon = Von.AddYears(-1), ZeitraumBis = Bis.AddYears(-1), ClosedByUserId = Guid.NewGuid() }
        });

        var service = CreateService();
        var result = await service.GetAllAsync();

        result.Should().HaveCount(2);
    }
}
