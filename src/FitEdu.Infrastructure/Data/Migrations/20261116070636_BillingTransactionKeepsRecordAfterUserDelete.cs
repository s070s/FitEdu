using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitEdu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BillingTransactionKeepsRecordAfterUserDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BillingTransactions_AspNetUsers_UserId",
                table: "BillingTransactions");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "BillingTransactions",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddForeignKey(
                name: "FK_BillingTransactions_AspNetUsers_UserId",
                table: "BillingTransactions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A billing record of a deleted account has no user. The old schema needs a user on every row, so a
            // rollback could only delete those records (tax records, kept by law) or give them an invented user.
            // It does neither: it stops with a message until someone has decided what to do with those rows.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [BillingTransactions] WHERE [UserId] IS NULL) " +
                "THROW 50000, 'Cannot roll back: BillingTransactions has rows whose account was deleted (UserId is NULL). " +
                "Rolling back would delete or falsify tax records. Export those rows, then delete them on purpose, and run the rollback again.', 1;");

            migrationBuilder.DropForeignKey(
                name: "FK_BillingTransactions_AspNetUsers_UserId",
                table: "BillingTransactions");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "BillingTransactions",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BillingTransactions_AspNetUsers_UserId",
                table: "BillingTransactions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
