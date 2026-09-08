using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Kuestencode.Faktura.Models;

namespace Kuestencode.Faktura.Data;

/// <summary>
/// DbContext für Faktura-spezifische Daten (Invoices, InvoiceItems, DownPayments).
/// Verwendet das Schema "faktura".
/// </summary>
public class FakturaDbContext : DbContext
{
    public FakturaDbContext(DbContextOptions<FakturaDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Suppress the PendingModelChangesWarning in .NET 9
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    // DbSets - nur Faktura-spezifische Entitäten
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<InvoiceItem> InvoiceItems { get; set; } = null!;
    public DbSet<DownPayment> DownPayments { get; set; } = null!;
    public DbSet<InvoiceAttachment> InvoiceAttachments { get; set; } = null!;
    public DbSet<InvoicePayment> InvoicePayments { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema-Trennung
        modelBuilder.HasDefaultSchema("faktura");

        // Invoice Configuration
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InvoiceNumber).IsUnique();
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.RelatedInvoiceId);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.DiscountValue).HasPrecision(18, 2);

            // CustomerId bleibt als FK, aber ohne Navigation Property zum Host-Schema
            entity.Property(e => e.CustomerId).IsRequired();

            entity.HasOne<Invoice>()
                .WithMany()
                .HasForeignKey(e => e.RelatedInvoiceId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Ignore(e => e.RelatedInvoice);

            // Relationships innerhalb des Faktura-Schemas
            entity.HasMany(e => e.Items)
                .WithOne(e => e.Invoice)
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.DownPayments)
                .WithOne(e => e.Invoice)
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Attachments)
                .WithOne(e => e.Invoice)
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Payments)
                .WithOne(e => e.Invoice)
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Customer Navigation Property ignorieren (Cross-Schema)
            entity.Ignore(e => e.Customer);
        });

        // InvoiceItem Configuration
        modelBuilder.Entity<InvoiceItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Quantity).HasPrecision(18, 3);
            entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
            entity.Property(e => e.VatRate).HasPrecision(5, 2);
        });

        // DownPayment Configuration
        modelBuilder.Entity<DownPayment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.SourceInvoiceId);
            entity.HasOne<Invoice>()
                .WithMany()
                .HasForeignKey(e => e.SourceInvoiceId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Ignore(e => e.SourceInvoice);
        });

        // InvoicePayment Configuration
        modelBuilder.Entity<InvoicePayment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // InvoiceAttachment Configuration
        modelBuilder.Entity<InvoiceAttachment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.FileSize).IsRequired();
            entity.Property(e => e.Data).IsRequired();
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.Entity.GetType().GetProperty("UpdatedAt") != null)
            {
                entry.Property("UpdatedAt").CurrentValue = DateTime.UtcNow;
            }

            if (entry.State == EntityState.Added && entry.Entity.GetType().GetProperty("CreatedAt") != null)
            {
                entry.Property("CreatedAt").CurrentValue = DateTime.UtcNow;
            }
        }
    }
}
