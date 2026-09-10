using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Werkbank.Saldo.Data.Repositories;

public class PeriodCloseRepository : IPeriodCloseRepository
{
    private readonly IDbContextFactory<SaldoDbContext> _factory;

    public PeriodCloseRepository(IDbContextFactory<SaldoDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<PeriodClose>> GetAllAsync()
    {
        await using var ctx = await _factory.CreateDbContextAsync();
        return await ctx.PeriodCloses
            .OrderByDescending(p => p.ClosedAt)
            .ToListAsync();
    }

    public async Task<PeriodClose?> GetForRangeAsync(DateOnly von, DateOnly bis)
    {
        await using var ctx = await _factory.CreateDbContextAsync();
        return await ctx.PeriodCloses
            .Where(p => p.ZeitraumVon == von && p.ZeitraumBis == bis)
            .OrderByDescending(p => p.ClosedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<PeriodClose> AddAsync(PeriodClose periodClose)
    {
        await using var ctx = await _factory.CreateDbContextAsync();
        periodClose.Id = Guid.NewGuid();
        await ctx.PeriodCloses.AddAsync(periodClose);
        await ctx.SaveChangesAsync();
        return periodClose;
    }
}
