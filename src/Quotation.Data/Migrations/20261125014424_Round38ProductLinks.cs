using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotation.Data.Migrations
{
    /// <inheritdoc />
    public partial class Round38ProductLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientKey",
                table: "Quotations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowProductLinks",
                table: "Quotations",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // existing quotations keep the links on (Round 38, PL-1)

            migrationBuilder.AddColumn<string>(
                name: "Link",
                table: "Products",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_ClientKey",
                table: "Quotations",
                column: "ClientKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotations_ClientKey",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "ClientKey",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "ShowProductLinks",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "Link",
                table: "Products");
        }
    }
}
