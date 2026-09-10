using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuestencode.Werkbank.Recepta.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNumberSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS recepta.""NumberSequences"" (
                    ""Id"" uuid NOT NULL,
                    ""SequenceKey"" character varying(100) NOT NULL,
                    ""CurrentValue"" bigint NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT ""PK_NumberSequences"" PRIMARY KEY (""Id"")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_NumberSequences_SequenceKey"" ON recepta.""NumberSequences""(""SequenceKey"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TABLE IF EXISTS recepta.""NumberSequences"";
            ");
        }
    }
}
