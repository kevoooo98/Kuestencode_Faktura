using Kuestencode.Werkbank.Acta.Data;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Werkbank.Acta.Tests.Data;

internal sealed class TestDbContextFactory : IDbContextFactory<ActaDbContext>
{
    private readonly DbContextOptions<ActaDbContext> _options;

    public TestDbContextFactory(DbContextOptions<ActaDbContext> options)
    {
        _options = options;
    }

    public ActaDbContext CreateDbContext() => new(_options);

    public static IDbContextFactory<ActaDbContext> CreateInMemory() => new TestDbContextFactory(
        new DbContextOptionsBuilder<ActaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
