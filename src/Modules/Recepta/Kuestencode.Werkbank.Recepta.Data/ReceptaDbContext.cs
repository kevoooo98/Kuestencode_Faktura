using Kuestencode.Core.Auditing;
using Kuestencode.Core.Auth;
using Kuestencode.Werkbank.Recepta.Domain.Entities;
using Kuestencode.Werkbank.Recepta.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kuestencode.Werkbank.Recepta.Data;

/// <summary>
/// DbContext für Recepta-spezifische Daten (Lieferanten, Belege, Dateien, OCR-Muster).
/// Verwendet das Schema "recepta".
/// </summary>
public class ReceptaDbContext : DbContext
{
    private readonly ICurrentUserAccessor? _currentUserAccessor;

    /// <summary>
    /// Entity-Typen und deren Felder, für die Änderungen im Audit-Log protokolliert werden
    /// (GoBD). <see cref="AuditChangeCollector"/> ignoriert alle anderen Entities/Felder.
    /// </summary>
    private static readonly Dictionary<Type, AuditedEntityConfig> AuditedProperties = new()
    {
        [typeof(Document)] = new AuditedEntityConfig(new[]
        {
            nameof(Document.Status), nameof(Document.InvoiceNumber), nameof(Document.InvoiceDate),
            nameof(Document.DueDate), nameof(Document.AmountNet), nameof(Document.AmountTax),
            nameof(Document.AmountGross), nameof(Document.SupplierId), nameof(Document.Category),
            nameof(Document.Notes), nameof(Document.OcrRawText)
        }),
        // Zahlungen werden nie geändert, nur angelegt/gelöscht — DetailedAddDelete sorgt dafür,
        // dass der Betrag im Audit-Log sichtbar bleibt statt einer leeren "Created"-Zeile.
        // ParentIdProperty/-EntityName ordnen den Eintrag dem Beleg zu, nicht der Zahlung selbst.
        [typeof(DocumentPayment)] = new AuditedEntityConfig(
            new[] { nameof(DocumentPayment.Amount), nameof(DocumentPayment.PaymentDate), nameof(DocumentPayment.Notes) },
            ParentIdProperty: nameof(DocumentPayment.DocumentId),
            ParentEntityName: nameof(Document),
            DetailedAddDelete: true)
    };

    public ReceptaDbContext(DbContextOptions<ReceptaDbContext> options, ICurrentUserAccessor? currentUserAccessor = null)
        : base(options)
    {
        _currentUserAccessor = currentUserAccessor;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    // DbSets
    public DbSet<Supplier> Suppliers { get; set; } = null!;
    public DbSet<Document> Documents { get; set; } = null!;
    public DbSet<DocumentFile> DocumentFiles { get; set; } = null!;
    public DbSet<SupplierOcrPattern> SupplierOcrPatterns { get; set; } = null!;
    public DbSet<DocumentProjectAllocation> DocumentProjectAllocations { get; set; } = null!;
    public DbSet<DocumentPayment> DocumentPayments { get; set; } = null!;
    public DbSet<DocumentActivityLog> DocumentActivityLogs { get; set; } = null!;
    public DbSet<NumberSequence> NumberSequences { get; set; } = null!;
    public DbSet<AuditLogEntry> AuditLogEntries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema-Trennung
        modelBuilder.HasDefaultSchema("recepta");

        // Supplier Configuration
        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.SupplierNumber).IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.DefaultCategory)
                .HasConversion<string>()
                .HasMaxLength(20);

            // Relationships
            entity.HasMany(e => e.Documents)
                .WithOne(e => e.Supplier)
                .HasForeignKey(e => e.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.OcrPatterns)
                .WithOne(e => e.Supplier)
                .HasForeignKey(e => e.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Document Configuration
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.DocumentNumber).IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Status als String speichern für bessere Lesbarkeit
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            // Category als String speichern
            entity.Property(e => e.Category)
                .HasConversion<string>()
                .HasMaxLength(20);

            // Decimal Precision
            entity.Property(e => e.AmountNet19).HasPrecision(18, 2);
            entity.Property(e => e.AmountTax19).HasPrecision(18, 2);
            entity.Property(e => e.AmountNet7).HasPrecision(18, 2);
            entity.Property(e => e.AmountTax7).HasPrecision(18, 2);
            entity.Property(e => e.AmountNet0).HasPrecision(18, 2);
            entity.Property(e => e.AmountNet).HasPrecision(18, 2);
            entity.Property(e => e.AmountTax).HasPrecision(18, 2);
            entity.Property(e => e.AmountGross).HasPrecision(18, 2);
            entity.Property(e => e.TaxRate).HasPrecision(5, 2);
            entity.Property(e => e.HasBeenAttached).HasDefaultValue(false);

            // Cascade delete für Dateianhänge
            entity.HasMany(e => e.Files)
                .WithOne(e => e.Document)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cascade delete für Projekt-Zuteilungen
            entity.HasMany(e => e.ProjectAllocations)
                .WithOne(e => e.Document)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cascade delete für Zahlungen
            entity.HasMany(e => e.Payments)
                .WithOne(e => e.Document)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // DocumentPayment Configuration
        modelBuilder.Entity<DocumentPayment>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.DocumentId);
        });

        // DocumentProjectAllocation Configuration
        modelBuilder.Entity<DocumentProjectAllocation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.AllocatedNet).HasPrecision(18, 2);
            entity.Property(e => e.AllocatedTax).HasPrecision(18, 2);
            entity.Property(e => e.AllocatedGross).HasPrecision(18, 2);

            // Pro Dokument kann ein Projekt nur einmal vorkommen
            entity.HasIndex(e => new { e.DocumentId, e.ProjectId }).IsUnique();
            // Schnelle Abfrage aller Belege eines Projekts
            entity.HasIndex(e => e.ProjectId);
        });

        // DocumentFile Configuration
        modelBuilder.Entity<DocumentFile>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // DocumentActivityLog Configuration
        modelBuilder.Entity<DocumentActivityLog>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.UserName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.DocumentNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.CreatedAt);
        });

        // SupplierOcrPattern Configuration
        modelBuilder.Entity<SupplierOcrPattern>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
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
    /// parallele Requests die Kette nicht gabeln. Ausnahme: der allererste Eintrag einer Tabelle
    /// hat keine Vorgänger-Zeile zum Sperren — dort fängt der Unique-Index auf SequenceNumber einen
    /// gleichzeitigen zweiten "ersten" Schreiber ab; dieser Fall wird per Retry aufgelöst (die Tabelle
    /// ist danach nicht mehr leer, der zweite Versuch sperrt regulär die inzwischen existierende Zeile).
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

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = await Database.BeginTransactionAsync(ct);
            try
            {
                var tip = await AuditHashChain.GetTipLockedAsync(Database, "recepta.\"AuditLogEntries\"", ct);
                AppendChainedEntries(pending, user, tip?.Hash ?? AuditHashChain.Genesis, (tip?.SequenceNumber ?? 0) + 1);
                await base.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (attempt < maxAttempts && IsUniqueSequenceNumberConflict(ex))
            {
                await transaction.RollbackAsync(ct);
                foreach (var entry in ChangeTracker.Entries<AuditLogEntry>().Where(e => e.State == EntityState.Added).ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }
    }

    private static bool IsUniqueSequenceNumberConflict(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };

    private void AppendChainedEntries(List<PendingAuditChange> pending, CurrentUser user, string previousHash, long nextSequenceNumber)
    {
        foreach (var change in pending)
        {
            var entityId = AuditChangeCollector.GetEntityId(change);
            var changedAt = AuditHashChain.TruncateToPostgresPrecision(DateTime.UtcNow);
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
