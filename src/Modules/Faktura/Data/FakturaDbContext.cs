using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Kuestencode.Core.Auditing;
using Kuestencode.Core.Auth;
using Kuestencode.Faktura.Models;

namespace Kuestencode.Faktura.Data;

/// <summary>
/// DbContext für Faktura-spezifische Daten (Invoices, InvoiceItems, DownPayments).
/// Verwendet das Schema "faktura".
/// </summary>
public class FakturaDbContext : DbContext
{
    private readonly ICurrentUserAccessor? _currentUserAccessor;

    /// <summary>
    /// Entity-Typen und deren Felder, für die Änderungen im Audit-Log protokolliert werden
    /// (GoBD). <see cref="AuditChangeCollector"/> ignoriert alle anderen Entities/Felder.
    /// </summary>
    private static readonly Dictionary<Type, AuditedEntityConfig> AuditedProperties = new()
    {
        [typeof(Invoice)] = new AuditedEntityConfig(new[]
        {
            nameof(Invoice.Status), nameof(Invoice.InvoiceDate), nameof(Invoice.DueDate),
            nameof(Invoice.Notes), nameof(Invoice.CustomerId), nameof(Invoice.DiscountType),
            nameof(Invoice.DiscountValue), nameof(Invoice.IsReverseCharge),
            nameof(Invoice.CancelledAt), nameof(Invoice.CancellationReason)
        }),
        // Zahlungen werden nie geändert, nur angelegt/gelöscht — DetailedAddDelete sorgt dafür,
        // dass der Betrag im Audit-Log sichtbar bleibt statt einer leeren "Created"-Zeile.
        // ParentIdProperty/-EntityName ordnen den Eintrag der Rechnung zu, nicht der Zahlung selbst.
        [typeof(InvoicePayment)] = new AuditedEntityConfig(
            new[] { nameof(InvoicePayment.Amount), nameof(InvoicePayment.PaymentDate), nameof(InvoicePayment.Notes) },
            ParentIdProperty: nameof(InvoicePayment.InvoiceId),
            ParentEntityName: nameof(Invoice),
            DetailedAddDelete: true)
    };

    public FakturaDbContext(DbContextOptions<FakturaDbContext> options, ICurrentUserAccessor? currentUserAccessor = null)
        : base(options)
    {
        _currentUserAccessor = currentUserAccessor;
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
    public DbSet<NumberSequence> NumberSequences { get; set; } = null!;
    public DbSet<AuditLogEntry> AuditLogEntries { get; set; } = null!;

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

        // NumberSequence Configuration
        modelBuilder.Entity<NumberSequence>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SequenceKey).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => e.SequenceKey).IsUnique();
        });

        // AuditLogEntry Configuration
        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EntityId).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(20).IsRequired();
            entity.Property(e => e.FieldName).HasMaxLength(100);
            entity.Property(e => e.ChangedByUserName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Hash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.PreviousHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.EntityName, e.EntityId });
            entity.HasIndex(e => e.SequenceNumber).IsUnique();
        });
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        var pending = AuditChangeCollector.CapturePending(ChangeTracker, AuditedProperties);

        var result = base.SaveChanges();

        if (pending.Count > 0)
        {
            AppendAuditEntriesAsync(pending, default).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        var pending = AuditChangeCollector.CapturePending(ChangeTracker, AuditedProperties);

        var result = await base.SaveChangesAsync(cancellationToken);

        if (pending.Count > 0)
        {
            await AppendAuditEntriesAsync(pending, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Hängt die gesammelten Audit-Zeilen verkettet (SHA-256-Hashkette, siehe AuditHashChain) an.
    /// Der Tip-Hash wird per SELECT...FOR UPDATE in einer eigenen Transaktion gesperrt, damit
    /// parallele Requests die Kette nicht gabeln.
    /// </summary>
    private async Task AppendAuditEntriesAsync(List<PendingAuditChange> pending, CancellationToken ct)
    {
        var user = _currentUserAccessor?.Get() ?? new CurrentUser(Guid.Empty, "System");

        if (!Database.IsRelational())
        {
            // Nicht-relationale Provider (EF InMemory in Tests) kennen weder Transaktionen noch
            // rohes SQL — Kette wird trotzdem gebildet, nur ohne Sperre gegen parallele Requests.
            // SequenceNumber wird applikationsseitig vergeben (nicht per DB-Identity, s. AuditHashChain).
            var existing = await AuditLogEntries.ToListAsync(ct);
            var tipEntry = existing.OrderByDescending(a => a.SequenceNumber).FirstOrDefault();
            var nextSeq = (tipEntry?.SequenceNumber ?? 0) + 1;
            AppendChainedEntries(pending, user, tipEntry?.Hash ?? AuditHashChain.Genesis, nextSeq);
            await base.SaveChangesAsync(ct);
            return;
        }

        await using var transaction = await Database.BeginTransactionAsync(ct);
        try
        {
            var tip = await AuditHashChain.GetTipLockedAsync(Database, "faktura.\"AuditLogEntries\"", ct);
            AppendChainedEntries(pending, user, tip?.Hash ?? AuditHashChain.Genesis, (tip?.SequenceNumber ?? 0) + 1);
            await base.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private void AppendChainedEntries(List<PendingAuditChange> pending, CurrentUser user, string previousHash, long nextSequenceNumber)
    {
        foreach (var change in pending)
        {
            var entityId = AuditChangeCollector.GetEntityId(change);
            var changedAt = DateTime.UtcNow;
            var hash = AuditHashChain.ComputeHash(
                previousHash, change.EntityName, entityId, change.Action,
                change.FieldName, change.OldValue, change.NewValue,
                user.UserId, user.UserName, changedAt);

            AuditLogEntries.Add(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                EntityName = change.EntityName,
                EntityId = entityId,
                Action = change.Action,
                FieldName = change.FieldName,
                OldValue = change.OldValue,
                NewValue = change.NewValue,
                ChangedByUserId = user.UserId,
                ChangedByUserName = user.UserName,
                ChangedAt = changedAt,
                SequenceNumber = nextSequenceNumber,
                PreviousHash = previousHash,
                Hash = hash
            });

            previousHash = hash;
            nextSequenceNumber++;
        }
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
