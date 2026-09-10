using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuestencode.Werkbank.Saldo.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKontoMappingOverrideVersionierung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE saldo.""KontoMappingOverrides""
                ADD COLUMN IF NOT EXISTS ""GueltigAb"" date NOT NULL DEFAULT CURRENT_DATE;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE saldo.""KontoMappingOverrides""
                ADD COLUMN IF NOT EXISTS ""GueltigBis"" date NULL;
            ");

            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS saldo.""IX_KontoMappingOverrides_Kontenrahmen_Kategorie"";
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_KontoMappingOverrides_Kontenrahmen_Kategorie""
                ON saldo.""KontoMappingOverrides"" (""Kontenrahmen"", ""Kategorie"")
                WHERE ""GueltigBis"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS saldo.""IX_KontoMappingOverrides_Kontenrahmen_Kategorie"";
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_KontoMappingOverrides_Kontenrahmen_Kategorie""
                ON saldo.""KontoMappingOverrides"" (""Kontenrahmen"", ""Kategorie"");
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE saldo.""KontoMappingOverrides""
                DROP COLUMN IF EXISTS ""GueltigBis"";
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE saldo.""KontoMappingOverrides""
                DROP COLUMN IF EXISTS ""GueltigAb"";
            ");
        }
    }
}
