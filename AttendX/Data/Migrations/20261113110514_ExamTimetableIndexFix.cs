using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendX.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExamTimetableIndexFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExamTimetables_ExamId_FacultyId_ExamDate_StartTime",
                table: "ExamTimetables");

            migrationBuilder.CreateIndex(
                name: "IX_ExamTimetables_ExamId_FacultyId_ExamDate_StartTime",
                table: "ExamTimetables",
                columns: new[] { "ExamId", "FacultyId", "ExamDate", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamTimetables_ExamId_RoomNo_ExamDate_StartTime",
                table: "ExamTimetables",
                columns: new[] { "ExamId", "RoomNo", "ExamDate", "StartTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExamTimetables_ExamId_FacultyId_ExamDate_StartTime",
                table: "ExamTimetables");

            migrationBuilder.DropIndex(
                name: "IX_ExamTimetables_ExamId_RoomNo_ExamDate_StartTime",
                table: "ExamTimetables");

            migrationBuilder.CreateIndex(
                name: "IX_ExamTimetables_ExamId_FacultyId_ExamDate_StartTime",
                table: "ExamTimetables",
                columns: new[] { "ExamId", "FacultyId", "ExamDate", "StartTime" },
                unique: true);
        }
    }
}
