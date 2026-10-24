using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ordena.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Documents archived before the payment status existed count as paid: only new ones are pending.
            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "Documents",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_IsPaid_IssueDate_Id",
                table: "Documents",
                columns: new[] { "IsPaid", "IssueDate", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_IsPaid_IssueDate_Id",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "Documents");
        }
    }
}
