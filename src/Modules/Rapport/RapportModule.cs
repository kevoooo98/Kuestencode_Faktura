using Kuestencode.Core.Interfaces;
using Kuestencode.Rapport.Data;
using Kuestencode.Rapport.Data.Repositories;
using Kuestencode.Rapport.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kuestencode.Rapport;

/// <summary>
/// Module definition for the Rapport module.
/// Provides extension methods for service registration.
/// </summary>
public static class RapportModule
{
    /// <summary>
    /// Adds all Rapport module services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddRapportModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Add DbContext with PostgreSQL (Rapport schema)
        // Use Factory pattern to avoid concurrency issues in Blazor Server
        services.AddDbContextFactory<RapportDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        // Register repositories
        services.AddScoped<TimeEntryRepository>();

        // Rapport core services
        services.AddScoped<SettingsService>();
        services.AddScoped<TimeRoundingService>();
        services.AddScoped<TimerService>();
        services.AddScoped<TimeEntryService>();
        services.AddScoped<DashboardService>();

        // Multi-user services
        services.AddScoped<IUserContextService, UserContextService>();
        services.AddScoped<TimeEntryAuditService>();
        services.AddScoped<TeamMemberCacheService>();

        // Background services
        services.AddHostedService<AutoStopTimerHostedService>();

        // Timesheet export services
        services.AddScoped<TimesheetExportService>();
        services.AddScoped<TimesheetPdfService>();
        services.AddScoped<TimesheetPreviewService>();
        services.AddScoped<TimesheetCsvService>();
        services.AddScoped<TimesheetEmailService>();
        services.AddScoped<IEmailEngine, Kuestencode.Shared.ApiClients.RemoteEmailEngine>();

        // Fallback project service
        services.TryAddScoped<IProjectService, MockProjectService>();

        return services;
    }

    /// <summary>
    /// Applies pending database migrations.
    /// </summary>
    public static async Task ApplyMigrationsAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RapportDbContext>();
        await context.Database.MigrateAsync();
    }
}

