using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitEdu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrimUnusedColumnsAndIndexesAddTestimonialRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nothing is lost silently: each column below is dropped only after SQL has checked that no row holds a
            // value other than the default. A row that does stops the migration with a message; the columns stay until
            // someone has decided what to do with that data.
            RefuseToDropWhenAnyRowHas(migrationBuilder, "Modules", "[IsPublished] = 1 OR [PublishedAt] IS NOT NULL", "IsPublished/PublishedAt");
            RefuseToDropWhenAnyRowHas(migrationBuilder, "Lessons", "[IsPublished] = 1 OR [PublishedAt] IS NOT NULL", "IsPublished/PublishedAt");
            RefuseToDropWhenAnyRowHas(migrationBuilder, "Categories", "[IsPublished] = 1 OR [PublishedAt] IS NOT NULL", "IsPublished/PublishedAt");
            RefuseToDropWhenAnyRowHas(migrationBuilder, "Exercises", "[IsDeleted] = 1 OR [DeletedAt] IS NOT NULL", "IsDeleted/DeletedAt");
            RefuseToDropWhenAnyRowHas(migrationBuilder, "BlogArticles", "[IsDeleted] = 1 OR [DeletedAt] IS NOT NULL", "IsDeleted/DeletedAt");
            RefuseToDropWhenAnyRowHas(migrationBuilder, "AssessmentDefinitions", "[IsDeleted] = 1 OR [DeletedAt] IS NOT NULL", "IsDeleted/DeletedAt");
            RefuseToDropWhenAnyRowHas(migrationBuilder, "Enrollments", "[ProgressJson] IS NOT NULL AND [ProgressJson] <> ''", "ProgressJson");

            migrationBuilder.DropIndex(
                name: "IX_LessonCompletions_UserId",
                table: "LessonCompletions");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "ProgressJson",
                table: "Enrollments");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "BlogArticles");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "BlogArticles");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "AssessmentDefinitions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AssessmentDefinitions");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Testimonials",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Testimonials");

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Modules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "Modules",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Lessons",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "Lessons",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Exercises",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Exercises",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProgressJson",
                table: "Enrollments",
                type: "nvarchar(max)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Categories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "Categories",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "BlogArticles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "BlogArticles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "AssessmentDefinitions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AssessmentDefinitions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_LessonCompletions_UserId",
                table: "LessonCompletions",
                column: "UserId");
        }

        private static void RefuseToDropWhenAnyRowHas(MigrationBuilder migrationBuilder, string table, string nonDefaultCondition, string columns)
        {
            migrationBuilder.Sql(
                $"IF EXISTS (SELECT 1 FROM [{table}] WHERE {nonDefaultCondition}) " +
                $"THROW 50000, 'Cannot drop {table}.{columns}: some rows hold a value other than the default. " +
                "Export or clear that data on purpose, then run the migration again.', 1;");
        }
    }
}
