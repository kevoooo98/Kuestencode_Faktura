using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using InvoiceApp.Data;
using InvoiceApp.Data.Repositories;
using InvoiceApp.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add Data Protection for password encryption
// Persist keys to a directory that survives container restarts
var keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
Directory.CreateDirectory(keysDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
    .SetApplicationName("InvoiceApp");

// Add DbContext with PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Repositories
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();

// Register Services
builder.Services.AddSingleton<PasswordEncryptionService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IPdfGeneratorService, PdfGeneratorService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IXRechnungService, XRechnungService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IPreviewService, PreviewService>();
builder.Services.AddScoped<IEmailValidationService, EmailValidationService>();

// Register Background Services
builder.Services.AddHostedService<InvoiceOverdueService>();

var app = builder.Build();

// Apply database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();

        logger.LogInformation("Applying database migrations...");

        // Use Migrate() instead of EnsureCreated() to support migrations
        context.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while applying migrations.");
        throw;
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
