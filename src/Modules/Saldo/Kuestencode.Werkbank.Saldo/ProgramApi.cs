using System.Globalization;
using System.Text;
using Kuestencode.Core.Interfaces;
using Kuestencode.Shared.ApiClients;
using Kuestencode.Shared.Contracts.Host;
using Kuestencode.Shared.Contracts.Navigation;
using Kuestencode.Shared.UI.Extensions;
using Kuestencode.Werkbank.Saldo.Services;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;
using QuestPDF.Infrastructure;

namespace Kuestencode.Werkbank.Saldo;

/// <summary>
/// Entry point for Saldo when running as a standalone Blazor Server + API service.
/// </summary>
public class ProgramApi
{
    public static async Task Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        QuestPDF.Settings.License = LicenseType.Community;

        var culture = new CultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseStaticWebAssets();
        builder.Configuration.AddJsonFile("appsettings.api.json", optional: true, reloadOnChange: true);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthorization();
        builder.Services.AddRazorPages();
        builder.Services.AddServerSideBlazor();

        builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
            Kuestencode.Shared.UI.Auth.PassThroughAuthStateProvider>();

        builder.Services.AddMudServices();
        builder.Services.AddSingleton<MudBlazor.MudLocalizer, Kuestencode.Shared.UI.GermanMudLocalizer>();

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Saldo API",
                Version = "v1",
                Description = "EÜR API for Kuestencode Werkbank"
            });
        });

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        });

        // Add Data Protection for password encryption
        var keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
        Directory.CreateDirectory(keysDirectory);
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
            .SetApplicationName("Kuestencode.Werkbank.Saldo");

        builder.Services.AddSingleton<IModuleRegistry, ApiModuleRegistry>();
        builder.Services.AddTransient<Kuestencode.Shared.UI.Handlers.AuthTokenDelegatingHandler>();

        // Host API Client
        builder.Services.AddHttpClient<IHostApiClient, HostApiClient>(client =>
        {
            var hostUrl = builder.Configuration.GetValue<string>("ServiceUrls:Host") ?? "http://localhost:8080";
            client.BaseAddress = new Uri(hostUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddHttpMessageHandler<Kuestencode.Shared.UI.Handlers.AuthTokenDelegatingHandler>();

        // Faktura API Client
        builder.Services.AddHttpClient<IFakturaApiClient, FakturaApiClient>(client =>
        {
            var fakturaUrl = builder.Configuration.GetValue<string>("ServiceUrls:Faktura") ?? "http://localhost:8081";
            client.BaseAddress = new Uri(fakturaUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Recepta Data Service (HttpClient für service-to-service, EÜR-Abfragen)
        builder.Services.AddHttpClient<IReceptaDataService, ReceptaDataService>(client =>
        {
            var receptaUrl = builder.Configuration.GetValue<string>("ServiceUrls:Recepta") ?? "http://localhost:8085";
            client.BaseAddress = new Uri(receptaUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Recepta API Client (für Datei-Downloads im Belege-Export)
        builder.Services.AddHttpClient<IReceptaApiClient, ReceptaApiClient>(client =>
        {
            var receptaUrl = builder.Configuration.GetValue<string>("ServiceUrls:Recepta") ?? "http://localhost:8085";
            client.BaseAddress = new Uri(receptaUrl);
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        builder.Services.AddScoped<ICompanyService, ApiCompanyService>();
        builder.Services.AddScoped<ICustomerService, ApiCustomerService>();

        builder.Services.AddSaldoModule(builder.Configuration);

        builder.Services.AddModuleHealthMonitor("Saldo", GetModuleInfo, builder.Configuration);

        var app = builder.Build();

        // Apply migrations
        var applyMigrationsSetting = builder.Configuration["APPLY_MIGRATIONS"];
        var applyMigrations = string.IsNullOrWhiteSpace(applyMigrationsSetting) ||
                              !string.Equals(applyMigrationsSetting, "false", StringComparison.OrdinalIgnoreCase);

        var migrationLogger = app.Services.GetRequiredService<ILogger<ProgramApi>>();
        migrationLogger.LogInformation("APPLY_MIGRATIONS='{Setting}' => ApplyMigrations={Apply}",
            applyMigrationsSetting ?? "<null>", applyMigrations);

        if (applyMigrations)
        {
            try
            {
                migrationLogger.LogInformation("Applying Saldo database migrations...");
                await SaldoModule.ApplyMigrationsAsync(app.Services);
                migrationLogger.LogInformation("Saldo database migrations applied successfully.");
            }
            catch (Exception ex)
            {
                migrationLogger.LogError(ex, "An error occurred while applying Saldo migrations.");
                throw;
            }
        }

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Saldo API v1"));
        }

        app.UsePathBase("/saldo");
        app.UseCors();
        app.UseStaticFiles();
        app.UseModuleHealthMonitor();
        app.UseRouting();
        app.UseMiddleware<Kuestencode.Shared.UI.Auth.JwtUserContextMiddleware>();
        app.UseAuthorization();
        app.MapRazorPages();
        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", module = "Saldo" }));
        app.MapBlazorHub("/_blazor");
        app.MapFallbackToPage("/{*path:nonfile}", "/_Host");
        app.MapFallbackToPage("/", "/_Host");

        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            await RegisterWithHost(app.Configuration, app.Services.GetRequiredService<ILogger<ProgramApi>>());
        });

        app.Run();
    }

    private static ModuleInfoDto GetModuleInfo(IConfiguration config)
    {
        var selfUrl = config.GetValue<string>("ServiceUrls:Self") ?? "http://localhost:8086";
        var moduleVersion = config["MODULE_VERSION"]
            ?? config["IMAGE_TAG"]
            ?? config["DOCKER_IMAGE_TAG"]
            ?? "dev";

        return new ModuleInfoDto
        {
            ModuleName = "Saldo",
            DisplayName = "Saldo",
            Version = moduleVersion,
            LogoUrl = "/saldo/company/logos/Saldo_Logo.png",
            HealthCheckUrl = $"{selfUrl}/saldo/health",
            NavigationItems = new List<NavItemDto>
            {
                new NavItemDto
                {
                    Label = "Saldo",
                    Href = "/saldo",
                    Icon = "/saldo/company/logos/Saldo_Logo.png",
                    Type = NavItemType.Link,
                    AllowedRoles = new List<UserRole> { UserRole.Buero, UserRole.Admin }
                },
                new NavItemDto
                {
                    Label = "Buchungen",
                    Href = "/saldo/buchungen",
                    Icon = "",
                    Type = NavItemType.Link,
                    AllowedRoles = new List<UserRole> { UserRole.Buero, UserRole.Admin }
                },
                new NavItemDto
                {
                    Label = "EÜR",
                    Href = "/saldo/euer",
                    Icon = "",
                    Type = NavItemType.Link,
                    AllowedRoles = new List<UserRole> { UserRole.Buero, UserRole.Admin }
                },
                new NavItemDto
                {
                    Label = "USt-Übersicht",
                    Href = "/saldo/ust",
                    Icon = "",
                    Type = NavItemType.Link,
                    AllowedRoles = new List<UserRole> { UserRole.Buero, UserRole.Admin }
                },
                new NavItemDto
                {
                    Label = "Export-Historie",
                    Href = "/saldo/export/historie",
                    Icon = "",
                    Type = NavItemType.Link,
                    AllowedRoles = new List<UserRole> { UserRole.Buero, UserRole.Admin }
                },
                new NavItemDto
                {
                    Label = "Saldo Konten",
                    Href = "/saldo/konten",
                    Icon = "",
                    Type = NavItemType.Settings,
                    Category = NavSettingsCategory.Buchhaltung,
                    AllowedRoles = new List<UserRole> { UserRole.Admin }
                },
                new NavItemDto
                {
                    Label = "Saldo Einstellungen",
                    Href = "/saldo/einstellungen",
                    Icon = "",
                    Type = NavItemType.Settings,
                    Category = NavSettingsCategory.Buchhaltung,
                    AllowedRoles = new List<UserRole> { UserRole.Admin }
                },
                new NavItemDto
                {
                    Label = "Kategorie-Mapping",
                    Href = "/saldo/einstellungen/mapping",
                    Icon = "",
                    Type = NavItemType.Settings,
                    Category = NavSettingsCategory.Buchhaltung,
                    AllowedRoles = new List<UserRole> { UserRole.Admin }
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
                logger.LogInformation("Saldo module registered successfully with Host at {HostUrl}", hostUrl);
            else
                logger.LogWarning("Failed to register Saldo module with Host. Status: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error registering Saldo module with Host");
        }
    }
}
