using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotation.Data.Migrations
{
    /// <inheritdoc />
    public partial class QuotationGstByBuyer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GstByBuyer",
                table: "Quotations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GstByBuyer",
                table: "Quotations");
        }
    }
}
