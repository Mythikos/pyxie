using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pyxie.Data.Sqlite.Migrations.Machine
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Folder = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsOpen = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Folder",
                table: "Projects",
                column: "Folder",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_IsOpen",
                table: "Projects",
                column: "IsOpen",
                unique: true,
                filter: "\"IsOpen\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
