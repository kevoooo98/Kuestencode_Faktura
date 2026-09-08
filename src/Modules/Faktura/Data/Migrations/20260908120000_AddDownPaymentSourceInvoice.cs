using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuestencode.Faktura.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDownPaymentSourceInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE faktura.""DownPayments"" ADD COLUMN IF NOT EXISTS ""SourceInvoiceId"" integer NULL;
                ALTER TABLE faktura.""DownPayments"" ADD CONSTRAINT ""FK_DownPayments_Invoices_SourceInvoiceId""
                    FOREIGN KEY (""SourceInvoiceId"") REFERENCES faktura.""Invoices"" (""Id"") ON DELETE SET NULL;
                CREATE INDEX IF NOT EXISTS ""IX_DownPayments_SourceInvoiceId"" ON faktura.""DownPayments""(""SourceInvoiceId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS faktura.""IX_DownPayments_SourceInvoiceId"";
                ALTER TABLE faktura.""DownPayments"" DROP CONSTRAINT IF EXISTS ""FK_DownPayments_Invoices_SourceInvoiceId"";
                ALTER TABLE faktura.""DownPayments"" DROP COLUMN IF EXISTS ""SourceInvoiceId"";
            ");
        }
    }
}
