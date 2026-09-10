using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuestencode.Faktura.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS faktura.""AuditLogEntries"" (
                    ""Id"" uuid NOT NULL,
                    ""EntityName"" character varying(100) NOT NULL,
                    ""EntityId"" character varying(50) NOT NULL,
                    ""Action"" character varying(20) NOT NULL,
                    ""FieldName"" character varying(100) NULL,
                    ""OldValue"" text NULL,
                    ""NewValue"" text NULL,
                    ""ChangedByUserId"" uuid NOT NULL,
                    ""ChangedByUserName"" character varying(200) NOT NULL,
                    ""ChangedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_AuditLogEntries"" PRIMARY KEY (""Id"")
                );
                CREATE INDEX IF NOT EXISTS ""IX_AuditLogEntries_EntityName_EntityId"" ON faktura.""AuditLogEntries""(""EntityName"", ""EntityId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS faktura.""AuditLogEntries"";
            ");
        }
    }
}
