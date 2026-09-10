using Kuestencode.Werkbank.Saldo.Data.Repositories;
using Kuestencode.Werkbank.Saldo.Domain.Dtos;
using Kuestencode.Werkbank.Saldo.Domain.Entities;

namespace Kuestencode.Werkbank.Saldo.Services;

public class PeriodCloseService : IPeriodCloseService
{
    private readonly IPeriodCloseRepository _repository;

    public PeriodCloseService(IPeriodCloseRepository repository)
    {
        _repository = repository;
    }

    public async Task<PeriodCloseDto> ClosePeriodAsync(DateOnly von, DateOnly bis, Guid closedByUserId, Guid? exportLogId)
    {
        var periodClose = new PeriodClose
        {
            ZeitraumVon = von,
            ZeitraumBis = bis,
            ClosedAt = DateTime.UtcNow,
            ClosedByUserId = closedByUserId,
            ExportLogId = exportLogId
        };

        var result = await _repository.AddAsync(periodClose);
        return MapToDto(result);
    }

    public async Task<PeriodCloseDto?> GetForRangeAsync(DateOnly von, DateOnly bis)
    {
        var result = await _repository.GetForRangeAsync(von, bis);
        return result == null ? null : MapToDto(result);
    }

    public async Task<List<PeriodCloseDto>> GetAllAsync()
    {
        var all = await _repository.GetAllAsync();
        return all.Select(MapToDto).ToList();
    }

    private static PeriodCloseDto MapToDto(PeriodClose p) => new()
    {
        Id = p.Id,
        ZeitraumVon = p.ZeitraumVon,
        ZeitraumBis = p.ZeitraumBis,
        ClosedAt = p.ClosedAt,
        ClosedByUserId = p.ClosedByUserId,
        ExportLogId = p.ExportLogId
    };
}
