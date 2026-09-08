using Kuestencode.Werkbank.Saldo.Data;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Werkbank.Saldo.Tests.Data;

internal sealed class TestDbContextFactory : IDbContextFactory<SaldoDbContext>
{
    private readonly DbContextOptions<SaldoDbContext> _options;

    public TestDbContextFactory(DbContextOptions<SaldoDbContext> options)
    {
        _options = options;
    }

    public SaldoDbContext CreateDbContext() => new(_options);

    public static IDbContextFactory<SaldoDbContext> CreateInMemory() => new TestDbContextFactory(
        new DbContextOptionsBuilder<SaldoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
