using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotation.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round21OwnershipAndPdfOpen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OpenPdfAfterSave",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // existing users: Save as PDF opens the PDF, as Open PDF did

            migrationBuilder.AddColumn<bool>(
                name: "EverApproved",
                table: "Quotations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Owner",
                table: "Quotations",
                type: "TEXT",
                nullable: true);

            // Quotations approved before this version: approved now (Approved = 2, Generated = 3), approved earlier and
            // edited back to draft, or approved and later cancelled (the history has the approval).
            migrationBuilder.Sql("""
                UPDATE Quotations SET EverApproved = 1
                WHERE ApprovedUtc IS NOT NULL OR Status IN (2, 3)
                   OR lower(Id) IN (SELECT lower(EntityId) FROM AuditLog
                                    WHERE EntityType = 'Quotation'
                                      AND Action IN ('QuotationApproved', 'QuotationApprovedWithStaleData', 'QuotationEditedAfterApproval'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpenPdfAfterSave",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EverApproved",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "Owner",
                table: "Quotations");
        }
    }
}
