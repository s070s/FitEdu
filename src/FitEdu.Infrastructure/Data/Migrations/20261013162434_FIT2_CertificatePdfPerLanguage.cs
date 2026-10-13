using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitEdu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FIT2_CertificatePdfPerLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PdfPathEl",
                table: "Certificates",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            // Existing PDFs were rendered in whichever language downloaded them first, which is
            // unknown, so forget them: each language renders again on its next download.
            migrationBuilder.Sql("UPDATE [Certificates] SET [PdfPath] = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PdfPathEl",
                table: "Certificates");
        }
    }
}
