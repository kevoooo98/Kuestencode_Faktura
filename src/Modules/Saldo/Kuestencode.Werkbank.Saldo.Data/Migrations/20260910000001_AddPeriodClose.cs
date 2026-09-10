using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuestencode.Werkbank.Saldo.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS saldo.""PeriodCloses"" (
                    ""Id"" uuid NOT NULL,
                    ""ZeitraumVon"" date NOT NULL,
                    ""ZeitraumBis"" date NOT NULL,
                    ""ClosedAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""ClosedByUserId"" uuid NOT NULL,
                    ""ExportLogId"" uuid NULL,
                    CONSTRAINT ""PK_PeriodCloses"" PRIMARY KEY (""Id"")
                );
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_PeriodCloses_ZeitraumVon_ZeitraumBis""
                ON saldo.""PeriodCloses"" (""ZeitraumVon"", ""ZeitraumBis"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS saldo.""PeriodCloses"";
            ");
        }
    }
}
