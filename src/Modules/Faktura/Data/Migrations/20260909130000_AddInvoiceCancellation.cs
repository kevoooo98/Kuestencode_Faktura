using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuestencode.Faktura.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE faktura.""Invoices"" ADD COLUMN IF NOT EXISTS ""CancelledAt"" timestamp with time zone NULL;
                ALTER TABLE faktura.""Invoices"" ADD COLUMN IF NOT EXISTS ""CancellationReason"" character varying(500) NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE faktura.""Invoices"" DROP COLUMN IF EXISTS ""CancellationReason"";
                ALTER TABLE faktura.""Invoices"" DROP COLUMN IF EXISTS ""CancelledAt"";
            ");
        }
    }
}
