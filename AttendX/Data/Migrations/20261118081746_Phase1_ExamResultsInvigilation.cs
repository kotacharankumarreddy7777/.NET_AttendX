using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AttendX.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1_ExamResultsInvigilation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExamWindowEnd",
                table: "Timetables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExamWindowStart",
                table: "Timetables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuspendedDuringExams",
                table: "Timetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvigilationOnly",
                table: "ExamTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultReleaseDate",
                table: "Exams",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "ResultsPublished",
                table: "Exams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SemesterTransitionLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromSemester = table.Column<int>(type: "integer", nullable: false),
                    ToSemester = table.Column<int>(type: "integer", nullable: false),
                    TransitionedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BranchId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemesterTransitionLogs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SemesterTransitionLogs");

            migrationBuilder.DropColumn(
                name: "ExamWindowEnd",
                table: "Timetables");

            migrationBuilder.DropColumn(
                name: "ExamWindowStart",
                table: "Timetables");

            migrationBuilder.DropColumn(
                name: "IsSuspendedDuringExams",
                table: "Timetables");

            migrationBuilder.DropColumn(
                name: "IsInvigilationOnly",
                table: "ExamTimetables");

            migrationBuilder.DropColumn(
                name: "ResultReleaseDate",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "ResultsPublished",
                table: "Exams");
        }
    }
}
