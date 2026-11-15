using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AttendX.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExamMarkLockAndSemestersAndNotificationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockStatus",
                table: "ExamMarks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "ExamMarks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockedBy",
                table: "ExamMarks",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModificationQueryAt",
                table: "ExamMarks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModificationQueryBy",
                table: "ExamMarks",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModificationQueryReason",
                table: "ExamMarks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModificationQueryStatus",
                table: "ExamMarks",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ModificationReviewNote",
                table: "ExamMarks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModificationReviewedAt",
                table: "ExamMarks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModificationReviewedBy",
                table: "ExamMarks",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModificationWindowEnd",
                table: "ExamMarks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModificationWindowStart",
                table: "ExamMarks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    To = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    EntityId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Semesters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AcademicYearId = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClassesEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExamStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExamEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Semesters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Semesters_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_Channel_Status",
                table: "NotificationLogs",
                columns: new[] { "Channel", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_CreatedAt",
                table: "NotificationLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Semesters_AcademicYearId_Number",
                table: "Semesters",
                columns: new[] { "AcademicYearId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationLogs");

            migrationBuilder.DropTable(
                name: "Semesters");

            migrationBuilder.DropColumn(
                name: "LockStatus",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "LockedBy",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "ModificationQueryAt",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "ModificationQueryBy",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "ModificationQueryReason",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "ModificationQueryStatus",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "ModificationReviewNote",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "ModificationReviewedAt",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "ModificationReviewedBy",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "ModificationWindowEnd",
                table: "ExamMarks");

            migrationBuilder.DropColumn(
                name: "ModificationWindowStart",
                table: "ExamMarks");
        }
    }
}
