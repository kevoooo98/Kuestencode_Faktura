using Kuestencode.Werkbank.Recepta.Data;
using Kuestencode.Werkbank.Recepta.Data.Repositories;
using Kuestencode.Werkbank.Recepta.Services;
using Kuestencode.Werkbank.Recepta.Services.Implementation;
using Kuestencode.Werkbank.Recepta.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kuestencode.Werkbank.Recepta;

/// <summary>
/// Module definition for the Recepta (Eingangsrechnungsverwaltung) module.
/// Provides extension methods for service registration.
/// </summary>
public static class ReceptaModule
{
    /// <summary>
    /// Adds all Recepta module services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddReceptaModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Kuestencode.Core.Auth.ICurrentUserAccessor, Kuestencode.Core.Auth.HttpContextCurrentUserAccessor>();

        // Add DbContext with PostgreSQL (Recepta-Schema)
        services.AddDbContext<ReceptaDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        // Register Repositories
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentAllocationRepository, DocumentAllocationRepository>();
        services.AddScoped<IDocumentFileRepository, DocumentFileRepository>();
        services.AddScoped<ISupplierOcrPatternRepository, SupplierOcrPatternRepository>();
        services.AddScoped<IDocumentPaymentRepository, DocumentPaymentRepository>();

        // Register Application Services
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentFileService, DocumentFileService>();
        services.AddScoped<IDocumentPaymentService, DocumentPaymentService>();
        services.AddScoped<IDocumentActivityRepository, DocumentActivityRepository>();
        services.AddScoped<IDocumentActivityService, DocumentActivityService>();

        // Register OCR Services
        services.AddSingleton<IOcrService, TesseractOcrService>();
        services.AddScoped<IOcrPatternService, OcrPatternService>();

        // Register XRechnung/ZUGFeRD Service
        services.AddSingleton<IXRechnungService, XRechnungService>();

        // Register Cached Project Service (5min cache for Acta projects)
        services.AddMemoryCache();
        services.AddScoped<ICachedProjectService, CachedProjectService>();

        return services;
    }

    /// <summary>
    /// Applies pending database migrations.
    /// </summary>
    public static async Task ApplyMigrationsAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ReceptaDbContext>();
        await context.Database.MigrateAsync();
    }
}
