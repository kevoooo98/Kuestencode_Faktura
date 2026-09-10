using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuestencode.Faktura.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceAttachmentFrozenSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE faktura.""InvoiceAttachments"" ADD COLUMN IF NOT EXISTS ""IsFrozenSnapshot"" boolean NOT NULL DEFAULT false;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE faktura.""InvoiceAttachments"" DROP COLUMN IF EXISTS ""IsFrozenSnapshot"";
            ");
        }
    }
}
