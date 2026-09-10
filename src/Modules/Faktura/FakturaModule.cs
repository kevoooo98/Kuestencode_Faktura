using Kuestencode.Faktura.Data;
using Kuestencode.Faktura.Data.Repositories;
using Kuestencode.Faktura.Services;
using Kuestencode.Faktura.Services.Email;
using Kuestencode.Faktura.Services.Pdf;
using Kuestencode.Faktura.Services.Pdf.Components;
using Kuestencode.Faktura.Services.Pdf.Layouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FakturaRepo = Kuestencode.Faktura.Data.Repositories;

namespace Kuestencode.Faktura;

/// <summary>
/// Module definition for the Faktura (Invoice) module.
/// Provides extension methods for service registration.
/// </summary>
public static class FakturaModule
{
    /// <summary>
    /// Adds all Faktura module services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddFakturaModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Kuestencode.Core.Auth.ICurrentUserAccessor, Kuestencode.Core.Auth.HttpContextCurrentUserAccessor>();

        // Add DbContext with PostgreSQL (Faktura-Schema)
        services.AddDbContext<FakturaDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        // Register Repositories
        services.AddScoped(typeof(FakturaRepo.IRepository<>), typeof(Repository<>));
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<FakturaRepo.IInvoicePaymentRepository, FakturaRepo.InvoicePaymentRepository>();

        // HINWEIS: Company und Customer Services kommen aus dem Host-Projekt
        // und müssen dort registriert werden

        // Register Faktura Services
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IInvoicePaymentService, InvoicePaymentService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IXRechnungService, XRechnungService>();
        services.AddScoped<IPreviewService, PreviewService>();
        services.AddScoped<IEmailValidationService, EmailValidationService>();

        // Register Email Services
        services.AddScoped<IEmailTemplateRenderer, HtmlEmailTemplateRenderer>();
        services.AddScoped<IEmailAttachmentBuilder, EmailAttachmentBuilder>();
        services.AddScoped<Kuestencode.Core.Interfaces.IEmailEngine, Kuestencode.Shared.ApiClients.RemoteEmailEngine>();
        services.AddScoped<IEmailMessageBuilder, EmailMessageBuilder>();
        services.AddScoped<Services.IEmailService, EmailService>();

        // Register PDF Services
        services.AddScoped<PdfTemplateEngine>();
        services.AddScoped<PdfQRCodeGenerator>();
        services.AddScoped<PdfSummaryBlockBuilder>();
        services.AddScoped<PdfDownPaymentDetailBuilder>();
        services.AddScoped<PdfPaymentInfoBuilder>();
        services.AddScoped<KlarLayoutRenderer>();
        services.AddScoped<StrukturiertLayoutRenderer>();
        services.AddScoped<BetontLayoutRenderer>();
        services.AddScoped<IPdfGeneratorService, PdfGeneratorService>();
        services.AddScoped<IPdfMergeService, PdfMergeService>();

        // Register Background Services
        services.AddHostedService<InvoiceOverdueService>();

        return services;
    }

    /// <summary>
    /// Applies pending database migrations.
    /// </summary>
    public static async Task ApplyMigrationsAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FakturaDbContext>();
        await context.Database.MigrateAsync();
    }
}
