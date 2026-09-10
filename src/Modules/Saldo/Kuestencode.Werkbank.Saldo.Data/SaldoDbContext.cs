using Kuestencode.Werkbank.Saldo.Domain.Entities;
using Kuestencode.Werkbank.Saldo.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kuestencode.Werkbank.Saldo.Data;

/// <summary>
/// DbContext für Saldo-spezifische Daten (EÜR, Konten, Mappings, Exporte).
/// Verwendet das Schema "saldo".
/// </summary>
public class SaldoDbContext : DbContext
{
    public SaldoDbContext(DbContextOptions<SaldoDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    public DbSet<SaldoSettings> SaldoSettings { get; set; } = null!;
    public DbSet<Konto> Konten { get; set; } = null!;
    public DbSet<KategorieKontoMapping> KategorieKontoMappings { get; set; } = null!;
    public DbSet<KontoMappingOverride> KontoMappingOverrides { get; set; } = null!;
    public DbSet<ExportLog> ExportLogs { get; set; } = null!;
    public DbSet<PeriodClose> PeriodCloses { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("saldo");

        // SaldoSettings
        modelBuilder.Entity<SaldoSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kontenrahmen).HasMaxLength(10).IsRequired();
            entity.Property(e => e.BeraterNummer).HasMaxLength(20);
            entity.Property(e => e.MandantenNummer).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // Konto
        modelBuilder.Entity<Konto>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Kontenrahmen, e.KontoNummer }).IsUnique();
            entity.Property(e => e.Kontenrahmen).HasMaxLength(10).IsRequired();
            entity.Property(e => e.KontoNummer).HasMaxLength(10).IsRequired();
            entity.Property(e => e.KontoBezeichnung).HasMaxLength(200).IsRequired();
            entity.Property(e => e.KontoTyp).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.UstSatz).HasPrecision(5, 2);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        // KategorieKontoMapping
        modelBuilder.Entity<KategorieKontoMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Kontenrahmen, e.ReceiptaKategorie }).IsUnique();
            entity.Property(e => e.Kontenrahmen).HasMaxLength(10).IsRequired();
            entity.Property(e => e.ReceiptaKategorie).HasMaxLength(30).IsRequired();
            entity.Property(e => e.KontoNummer).HasMaxLength(10).IsRequired();
            entity.Property(e => e.IsCustom).HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.Konto)
                .WithMany(k => k.Mappings)
                .HasForeignKey(e => new { e.Kontenrahmen, e.KontoNummer })
                .HasPrincipalKey(k => new { k.Kontenrahmen, k.KontoNummer })
                .OnDelete(DeleteBehavior.Restrict);
        });

        // KontoMappingOverride
        modelBuilder.Entity<KontoMappingOverride>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Maximal eine offene (aktive) Version pro Kontenrahmen+Kategorie.
            entity.HasIndex(e => new { e.Kontenrahmen, e.Kategorie })
                .IsUnique()
                .HasFilter("\"GueltigBis\" IS NULL");
            entity.Property(e => e.Kontenrahmen).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Kategorie).HasMaxLength(30).IsRequired();
            entity.Property(e => e.KontoNummer).HasMaxLength(10).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // ExportLog
        modelBuilder.Entity<ExportLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExportTyp).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.DateiName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ExportedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // PeriodClose
        modelBuilder.Entity<PeriodClose>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ZeitraumVon, e.ZeitraumBis });
            entity.Property(e => e.ClosedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
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
                entry.Property("UpdatedAt").CurrentValue = DateTime.UtcNow;

            if (entry.State == EntityState.Added && entry.Entity.GetType().GetProperty("CreatedAt") != null)
                entry.Property("CreatedAt").CurrentValue = DateTime.UtcNow;
        }
    }
}
