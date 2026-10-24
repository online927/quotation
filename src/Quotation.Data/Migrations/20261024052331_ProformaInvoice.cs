using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotation.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProformaInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotations_FinancialYearStart_Sequence",
                table: "Quotations");

            migrationBuilder.AddColumn<string>(
                name: "DeliveryNote",
                table: "Quotations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeliveryNoteDate",
                table: "Quotations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchDocNo",
                table: "Quotations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DocType",
                table: "Quotations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PartNumber",
                table: "QuotationLines",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_DocType_FinancialYearStart_Sequence",
                table: "Quotations",
                columns: new[] { "DocType", "FinancialYearStart", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotations_DocType_FinancialYearStart_Sequence",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DeliveryNote",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DeliveryNoteDate",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DispatchDocNo",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DocType",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "PartNumber",
                table: "QuotationLines");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_FinancialYearStart_Sequence",
                table: "Quotations",
                columns: new[] { "FinancialYearStart", "Sequence" },
                unique: true);
        }
    }
}
