using System.Globalization;
using Kuestencode.Core.Interfaces;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.Contracts.Navigation;
using Kuestencode.Shared.UI.Extensions;
using Kuestencode.Werkbank.Recepta.Services;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;

namespace Kuestencode.Werkbank.Recepta;

/// <summary>
/// Entry point for Recepta when running as a standalone Blazor Server + API service.
/// This is used when Recepta runs in its own Docker container.
/// </summary>
public class ProgramApi
{
    public static async Task Main(string[] args)
    {
        // Deutsche Lokalisierung für MudBlazor DatePicker etc.
        var culture = new CultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseStaticWebAssets();
        builder.Configuration.AddJsonFile("appsettings.api.json", optional: true, reloadOnChange: true);
        builder.Configuration.AddEnvironmentVariables();

        // Add HttpContextAccessor for authentication
        builder.Services.AddHttpContextAccessor();

        // Authentication is handled by PassThroughAuthStateProvider reading the JWT
        // from werkbank_auth_cookie directly. No ASP.NET Cookie Authentication needed.
        builder.Services.AddAuthorization();

        // Add Blazor Server + Razor Pages
        builder.Services.AddRazorPages();
        builder.Services.AddServerSideBlazor();

        // Add PassThrough AuthenticationStateProvider for modules
        builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
            Kuestencode.Shared.UI.Auth.PassThroughAuthStateProvider>();

        // Add MudBlazor
        builder.Services.AddMudServices();
        builder.Services.AddSingleton<MudBlazor.MudLocalizer, Kuestencode.Shared.UI.GermanMudLocalizer>();

        // Add Controllers for API
        builder.Services.AddControllers();
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 52_428_800; // 50 MB
        });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Recepta API",
                Version = "v1",
                Description = "Eingangsrechnungsverwaltung API for Kuestencode Werkbank"
            });
        });

        // Add CORS for cross-origin requests from Host
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        // Add Data Protection for password encryption
        var keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
        Directory.CreateDirectory(keysDirectory);
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
            .SetApplicationName("Kuestencode.Werkbank.Recepta");

        // Add Module Registry (stub for API mode - modules are registered via HTTP)
        builder.Services.AddSingleton<IModuleRegistry, ApiModuleRegistry>();

        // Add AuthTokenDelegatingHandler for forwarding Authorization headers
        builder.Services.AddTransient<Kuestencode.Shared.UI.Handlers.AuthTokenDelegatingHandler>();

        // Add Host API Client with AuthTokenDelegatingHandler (must be registered before API-based services)
        builder.Services.AddHttpClient<IHostApiClient, HostApiClient>(client =>
        {
            var hostUrl = builder.Configuration.GetValue<string>("ServiceUrls:Host") ?? "http://localhost:8080";
            client.BaseAddress = new Uri(hostUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<Kuestencode.Shared.UI.Handlers.AuthTokenDelegatingHandler>();

        builder.Services.AddTransient<ModuleAvailabilityService>();

        // Add API-based implementations of Host services (Company, Customer)
        builder.Services.AddScoped<ICompanyService, ApiCompanyService>();
        builder.Services.AddScoped<ICustomerService, ApiCustomerService>();

        // Add Recepta Services (includes DbContext, Repositories, etc.)
        builder.Services.AddReceptaModule(builder.Configuration);

        // Add Module Health Monitor - re-registers if no health check received within 60 seconds
        builder.Services.AddModuleHealthMonitor("Recepta", GetModuleInfo, builder.Configuration);

        var app = builder.Build();

        // Apply migrations
        var applyMigrationsSetting = builder.Configuration["APPLY_MIGRATIONS"];
        var applyMigrations = string.IsNullOrWhiteSpace(applyMigrationsSetting) ||
                              !string.Equals(applyMigrationsSetting, "false", StringComparison.OrdinalIgnoreCase);

        var migrationLogger = app.Services.GetRequiredService<ILogger<ProgramApi>>();
        migrationLogger.LogInformation("APPLY_MIGRATIONS='{ApplyMigrationsSetting}' => ApplyMigrations={ApplyMigrations}",
            applyMigrationsSetting ?? "<null>", applyMigrations);

        if (applyMigrations)
        {
            try
            {
                migrationLogger.LogInformation("Applying Recepta database migrations...");
                await ReceptaModule.ApplyMigrationsAsync(app.Services);
                migrationLogger.LogInformation("Recepta database migrations applied successfully.");
            }
            catch (Exception ex)
            {
                migrationLogger.LogError(ex, "An error occurred while applying Recepta migrations.");
                throw;
            }
        }

        // Demo-Seed (nur wenn DEMO_SEED=true)
        var demoSeed = builder.Configuration.GetValue("DEMO_SEED", false);
        if (demoSeed)
        {
            try
            {
                migrationLogger.LogInformation("Seeding Recepta demo data...");
                await Kuestencode.Werkbank.Recepta.Data.DemoSeedData.SeedAsync(app.Services);
                migrationLogger.LogInformation("Recepta demo data seeded successfully.");
            }
            catch (Exception ex)
            {
                migrationLogger.LogError(ex, "An error occurred while seeding Recepta demo data.");
            }
        }

        // Configure pipeline
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Recepta API v1");
            });
        }

        // Set path base for reverse proxy compatibility
        app.UsePathBase("/recepta");

        app.UseCors();
        app.UseStaticFiles();

        // Add Health Check Tracker Middleware
        app.UseModuleHealthMonitor();

        app.UseRouting();

        // Populate HttpContext.User from the werkbank JWT for API/controller requests
        app.UseMiddleware<Kuestencode.Shared.UI.Auth.JwtUserContextMiddleware>();

        // Add Authorization
        app.UseAuthorization();

        app.MapRazorPages();

        // Map API Controllers
        app.MapControllers();

        // Health check endpoint for module registry
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", module = "Recepta" }));

        // Map Blazor Hub (path relative to path base)
        app.MapBlazorHub("/_blazor");

        // Map Recepta pages (paths are relative to path base /recepta)
        app.MapFallbackToPage("/{*path:nonfile}", "/_Host");
        app.MapFallbackToPage("/", "/_Host");

        // Register with Host on startup
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000); // Wait for Host to be ready
            await RegisterWithHost(app.Configuration, app.Services.GetRequiredService<ILogger<ProgramApi>>());
        });

        app.Run();
    }

    private static ModuleInfoDto GetModuleInfo(IConfiguration config)
    {
        var selfUrl = config.GetValue<string>("ServiceUrls:Self") ?? "http://localhost:8085";
        var moduleVersion = config["MODULE_VERSION"]
            ?? config["IMAGE_TAG"]
            ?? config["DOCKER_IMAGE_TAG"]
            ?? "dev";

        return new ModuleInfoDto
        {
            ModuleName = "Recepta",
            DisplayName = "Recepta",
            Version = moduleVersion,
            LogoUrl = "/recepta/company/logos/Recepta_Logo.png",
            HealthCheckUrl = $"{selfUrl}/recepta/health",
            NavigationItems = new List<NavItemDto>
            {
                new NavItemDto
                {
                    Label = "Recepta",
                    Href = "/recepta",
                    Icon = "/recepta/company/logos/Recepta_Logo.png",
                    Type = NavItemType.Link,
                    AllowedRoles = new List<UserRole> { UserRole.Buero, UserRole.Admin }
                },
                new NavItemDto
                {
                    Label = "Belege",
                    Href = "/recepta/belege",
                    Icon = "",
                    Type = NavItemType.Link,
                    AllowedRoles = new List<UserRole> { UserRole.Buero, UserRole.Admin }
                },
                new NavItemDto
                {
                    Label = "Lieferanten",
                    Href = "/recepta/lieferanten",
                    Icon = "",
                    Type = NavItemType.Link,
                    AllowedRoles = new List<UserRole> { UserRole.Buero, UserRole.Admin }
                }
            }
        };
    }

    private static async Task RegisterWithHost(IConfiguration config, ILogger logger)
    {
        try
        {
            var hostUrl = config.GetValue<string>("ServiceUrls:Host") ?? "http://localhost:8080";
            using var client = new HttpClient { BaseAddress = new Uri(hostUrl) };

            var moduleInfo = GetModuleInfo(config);

            var response = await client.PostAsJsonAsync("/api/modules/register", moduleInfo);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Recepta module registered successfully with Host at {HostUrl}", hostUrl);
            }
            else
            {
                logger.LogWarning("Failed to register Recepta module with Host. Status: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error registering Recepta module with Host");
        }
    }
}
