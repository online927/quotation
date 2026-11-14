using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotation.Data.Migrations
{
    /// <inheritdoc />
    public partial class FormaterMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormaterMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Typed = table.Column<string>(type: "TEXT", nullable: false),
                    TallyId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormaterMatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormaterMatches_Kind_Typed",
                table: "FormaterMatches",
                columns: new[] { "Kind", "Typed" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormaterMatches");
        }
    }
}
