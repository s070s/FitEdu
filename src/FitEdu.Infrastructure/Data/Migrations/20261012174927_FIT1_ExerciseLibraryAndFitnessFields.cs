using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FitEdu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FIT1_ExerciseLibraryAndFitnessFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuoteTextEl",
                table: "Testimonials",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoleOrTitleEl",
                table: "Testimonials",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEl",
                table: "SubscriptionPlans",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverExerciseSlug",
                table: "Courses",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Equipment",
                table: "Courses",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedWeeks",
                table: "Courses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Goal",
                table: "Courses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "Courses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SessionsPerWeek",
                table: "Courses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Goal",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameEl = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Force = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Mechanic = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Equipment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PrimaryMuscles = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SecondaryMuscles = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    InstructionsEn = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    InstructionsEl = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ImageCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LessonExercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Sets = table.Column<int>(type: "int", nullable: true),
                    Reps = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RestSeconds = table.Column<int>(type: "int", nullable: true),
                    NotesEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NotesEl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonExercises_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonExercises_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Courses_Level_Goal",
                table: "Courses",
                columns: new[] { "Level", "Goal" });

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_Category_Level",
                table: "Exercises",
                columns: new[] { "Category", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_Slug",
                table: "Exercises",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonExercises_ExerciseId",
                table: "LessonExercises",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonExercises_LessonId_SortOrder",
                table: "LessonExercises",
                columns: new[] { "LessonId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonExercises");

            migrationBuilder.DropTable(
                name: "Exercises");

            migrationBuilder.DropIndex(
                name: "IX_Courses_Level_Goal",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "QuoteTextEl",
                table: "Testimonials");

            migrationBuilder.DropColumn(
                name: "RoleOrTitleEl",
                table: "Testimonials");

            migrationBuilder.DropColumn(
                name: "DescriptionEl",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "CoverExerciseSlug",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Equipment",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "EstimatedWeeks",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Goal",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "SessionsPerWeek",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "Goal",
                table: "AspNetUsers");
        }
    }
}
