using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendX.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExamTimetableFacultySectionUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExamTimetables_FacultyId",
                table: "ExamTimetables");

            migrationBuilder.DropIndex(
                name: "IX_ExamTimetables_SectionId",
                table: "ExamTimetables");

            migrationBuilder.CreateIndex(
                name: "IX_ExamTimetables_FacultyId_ExamDate_StartTime",
                table: "ExamTimetables",
                columns: new[] { "FacultyId", "ExamDate", "StartTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamTimetables_SectionId_ExamDate_StartTime",
                table: "ExamTimetables",
                columns: new[] { "SectionId", "ExamDate", "StartTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExamTimetables_FacultyId_ExamDate_StartTime",
                table: "ExamTimetables");

            migrationBuilder.DropIndex(
                name: "IX_ExamTimetables_SectionId_ExamDate_StartTime",
                table: "ExamTimetables");

            migrationBuilder.CreateIndex(
                name: "IX_ExamTimetables_FacultyId",
                table: "ExamTimetables",
                column: "FacultyId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamTimetables_SectionId",
                table: "ExamTimetables",
                column: "SectionId");
        }
    }
}
