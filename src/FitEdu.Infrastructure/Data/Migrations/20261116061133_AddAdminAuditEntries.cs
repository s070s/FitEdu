using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitEdu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TargetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditEntries_OccurredAt",
                table: "AdminAuditEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditEntries_TargetUserId_OccurredAt",
                table: "AdminAuditEntries",
                columns: new[] { "TargetUserId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAuditEntries");
        }
    }
}
