using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuestencode.Faktura.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogHashChain : Migration
    {
        private const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SequenceNumber wird applikationsseitig vergeben (nicht per DB-Identity), damit die
            // Vergabe unter derselben SELECT...FOR UPDATE-Sperre wie der Hash läuft (AuditHashChain)
            // und über relationale wie nicht-relationale Provider hinweg identisch funktioniert.
            migrationBuilder.Sql(@"
                ALTER TABLE faktura.""AuditLogEntries""
                ADD COLUMN IF NOT EXISTS ""SequenceNumber"" bigint;
            ");

            // Bereits vorhandene Zeilen (aus der Zeit vor der Hashkette) bekommen eindeutige,
            // nach Zeitpunkt geordnete Nummern — nötig, damit der anschließende Unique-Index nicht
            // an mehreren Zeilen mit demselben Default-Wert scheitert.
            migrationBuilder.Sql(@"
                UPDATE faktura.""AuditLogEntries"" t
                SET ""SequenceNumber"" = sub.rn
                FROM (
                    SELECT ""Id"", ROW_NUMBER() OVER (ORDER BY ""ChangedAt"") AS rn
                    FROM faktura.""AuditLogEntries""
                    WHERE ""SequenceNumber"" IS NULL
                ) sub
                WHERE t.""Id"" = sub.""Id"";
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE faktura.""AuditLogEntries""
                ALTER COLUMN ""SequenceNumber"" SET NOT NULL;
            ");

            migrationBuilder.Sql($@"
                ALTER TABLE faktura.""AuditLogEntries""
                ADD COLUMN IF NOT EXISTS ""Hash"" character varying(64) NOT NULL DEFAULT '{Genesis}';
            ");

            migrationBuilder.Sql($@"
                ALTER TABLE faktura.""AuditLogEntries""
                ADD COLUMN IF NOT EXISTS ""PreviousHash"" character varying(64) NOT NULL DEFAULT '{Genesis}';
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AuditLogEntries_SequenceNumber""
                ON faktura.""AuditLogEntries"" (""SequenceNumber"");
            ");

            // Append-only: UPDATE/DELETE auf der Audit-Tabelle wird von der DB selbst verweigert
            // (GoBD Unveränderbarkeit). Schützt gegen versehentliche/App-seitige Änderungen — ein
            // Postgres-Superuser kann den Trigger technisch entfernen, aber genau dagegen wirkt die
            // Hashkette (siehe AuditHashChain): eine Manipulation bleibt danach nachweisbar.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION faktura.audit_log_entries_append_only() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'faktura.AuditLogEntries ist append-only (GoBD) - UPDATE/DELETE nicht erlaubt.';
                END;
                $$ LANGUAGE plpgsql;
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_audit_log_entries_append_only ON faktura.""AuditLogEntries"";
            ");

            migrationBuilder.Sql(@"
                CREATE TRIGGER trg_audit_log_entries_append_only
                BEFORE UPDATE OR DELETE ON faktura.""AuditLogEntries""
                FOR EACH ROW EXECUTE FUNCTION faktura.audit_log_entries_append_only();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_audit_log_entries_append_only ON faktura.""AuditLogEntries"";
            ");

            migrationBuilder.Sql(@"
                DROP FUNCTION IF EXISTS faktura.audit_log_entries_append_only();
            ");

            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS faktura.""IX_AuditLogEntries_SequenceNumber"";
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE faktura.""AuditLogEntries""
                DROP COLUMN IF EXISTS ""PreviousHash"";
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE faktura.""AuditLogEntries""
                DROP COLUMN IF EXISTS ""Hash"";
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE faktura.""AuditLogEntries""
                DROP COLUMN IF EXISTS ""SequenceNumber"";
            ");
        }
    }
}
