using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotation.Data.Migrations
{
    /// <inheritdoc />
    public partial class GstModeDeleteUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WithoutGstNext",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Quotations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedNumber",
                table: "Quotations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedSequence",
                table: "Quotations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "Quotations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RoundOffTotal",
                table: "Quotations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TaxPresentation",
                table: "Quotations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "WithoutGst",
                table: "Quotations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_DeletedUtc",
                table: "Quotations",
                column: "DeletedUtc");

            // Quotations saved before this version were calculated with the settings of that time: keep their totals.
            migrationBuilder.Sql("UPDATE Quotations SET TaxPresentation = 1 WHERE TaxTotal <> 0;");
            migrationBuilder.Sql("UPDATE Quotations SET RoundOffTotal = 1 WHERE RoundOff <> 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotations_DeletedUtc",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "WithoutGstNext",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DeletedNumber",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DeletedSequence",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "RoundOffTotal",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "TaxPresentation",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "WithoutGst",
                table: "Quotations");
        }
    }
}
