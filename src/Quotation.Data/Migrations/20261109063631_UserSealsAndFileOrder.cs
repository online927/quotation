using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quotation.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserSealsAndFileOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasSealImage",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PdfFileOrder",
                table: "Users",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "NumberFirst");

            migrationBuilder.AddColumn<string>(
                name: "SealPinHash",
                table: "Users",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasSealImage",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PdfFileOrder",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SealPinHash",
                table: "Users");
        }
    }
}
