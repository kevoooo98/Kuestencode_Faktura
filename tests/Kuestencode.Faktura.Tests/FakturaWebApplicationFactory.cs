using Kuestencode.Faktura.Api;
using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Tests.TestDoubles;
using Kuestencode.Shared.ApiClients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Kuestencode.Faktura.Tests;

public sealed class FakturaWebApplicationFactory : WebApplicationFactory<ProgramApi>
{
    private readonly string _dbName = $"faktura-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APPLY_MIGRATIONS"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FakturaDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FakturaDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<FakturaDbContext>();

            var npgsqlDescriptors = services
                .Where(d => d.ImplementationType?.Assembly.GetName().Name?.Contains("Npgsql") == true
                         || d.ServiceType.Assembly.GetName().Name?.Contains("Npgsql") == true)
                .ToList();
            foreach (var descriptor in npgsqlDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<FakturaDbContext>(options => options.UseInMemoryDatabase(_dbName));

            services.RemoveAll<IHostedService>();

            services.RemoveAll<IHostApiClient>();
            services.AddSingleton<IHostApiClient, FakeHostApiClient>();
        });
    }
}
