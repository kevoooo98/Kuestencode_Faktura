using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Werkbank.Saldo.Data.Repositories;

public class KontoMappingOverrideRepository : IKontoMappingOverrideRepository
{
    private readonly IDbContextFactory<SaldoDbContext> _factory;

    public KontoMappingOverrideRepository(IDbContextFactory<SaldoDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<KontoMappingOverride>> GetAllAsync(string kontenrahmen)
    {
        await using var ctx = await _factory.CreateDbContextAsync();
        return await ctx.KontoMappingOverrides
            .Where(o => o.Kontenrahmen == kontenrahmen && o.GueltigBis == null)
            .OrderBy(o => o.Kategorie)
            .ToListAsync();
    }

    public async Task<KontoMappingOverride?> GetByKategorieAsync(string kontenrahmen, string kategorie, DateOnly asOfDate)
    {
        await using var ctx = await _factory.CreateDbContextAsync();
        return await ctx.KontoMappingOverrides
            .FirstOrDefaultAsync(o => o.Kontenrahmen == kontenrahmen && o.Kategorie == kategorie
                && o.GueltigAb <= asOfDate
                && (o.GueltigBis == null || o.GueltigBis >= asOfDate));
    }

    public async Task<KontoMappingOverride> SetOverrideAsync(string kontenrahmen, string kategorie, string kontoNummer, DateOnly gueltigAb)
    {
        await using var ctx = await _factory.CreateDbContextAsync();
        var offen = await ctx.KontoMappingOverrides
            .FirstOrDefaultAsync(o => o.Kontenrahmen == kontenrahmen && o.Kategorie == kategorie && o.GueltigBis == null);

        if (offen != null)
        {
            offen.GueltigBis = gueltigAb.AddDays(-1);
            offen.UpdatedAt = DateTime.UtcNow;
        }

        var neu = new KontoMappingOverride
        {
            Id = Guid.NewGuid(),
            Kontenrahmen = kontenrahmen,
            Kategorie = kategorie,
            KontoNummer = kontoNummer,
            GueltigAb = gueltigAb,
            GueltigBis = null
        };

        ctx.KontoMappingOverrides.Add(neu);
        await ctx.SaveChangesAsync();
        return neu;
    }

    public async Task DeleteAsync(string kontenrahmen, string kategorie, DateOnly asOfDate)
    {
        await using var ctx = await _factory.CreateDbContextAsync();
        var offen = await ctx.KontoMappingOverrides
            .FirstOrDefaultAsync(o => o.Kontenrahmen == kontenrahmen && o.Kategorie == kategorie && o.GueltigBis == null);

        if (offen != null)
        {
            offen.GueltigBis = asOfDate.AddDays(-1);
            offen.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }
    }
}
