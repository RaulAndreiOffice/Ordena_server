using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordena.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NameNorm = table.Column<string>(type: "TEXT", nullable: false),
                    FolderName = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PartnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocNumber = table.Column<string>(type: "TEXT", nullable: false),
                    DocNumberNorm = table.Column<string>(type: "TEXT", nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    OriginalName = table.Column<string>(type: "TEXT", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FileOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromPath = table.Column<string>(type: "TEXT", nullable: false),
                    ToPath = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileOperations_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocNumberNorm",
                table: "Documents",
                column: "DocNumberNorm");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_IssueDate",
                table: "Documents",
                column: "IssueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_PartnerId_DocNumberNorm",
                table: "Documents",
                columns: new[] { "PartnerId", "DocNumberNorm" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_RelativePath",
                table: "Documents",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Sha256",
                table: "Documents",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileOperations_DocumentId",
                table: "FileOperations",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Partners_FolderName",
                table: "Partners",
                column: "FolderName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_NameNorm",
                table: "Partners",
                column: "NameNorm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileOperations");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Partners");
        }
    }
}
