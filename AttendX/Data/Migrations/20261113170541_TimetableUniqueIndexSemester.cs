using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendX.Data.Migrations
{
    /// <inheritdoc />
    public partial class TimetableUniqueIndexSemester : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Timetables_AcademicYearId_FacultyId_Day_Period",
                table: "Timetables");

            migrationBuilder.DropIndex(
                name: "IX_Timetables_AcademicYearId_SectionId_Day_Period",
                table: "Timetables");

            migrationBuilder.CreateIndex(
                name: "IX_Timetables_AcademicYearId_FacultyId_Day_Period_Semester",
                table: "Timetables",
                columns: new[] { "AcademicYearId", "FacultyId", "Day", "Period", "Semester" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Timetables_AcademicYearId_SectionId_Day_Period_Semester",
                table: "Timetables",
                columns: new[] { "AcademicYearId", "SectionId", "Day", "Period", "Semester" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Timetables_AcademicYearId_FacultyId_Day_Period_Semester",
                table: "Timetables");

            migrationBuilder.DropIndex(
                name: "IX_Timetables_AcademicYearId_SectionId_Day_Period_Semester",
                table: "Timetables");

            migrationBuilder.CreateIndex(
                name: "IX_Timetables_AcademicYearId_FacultyId_Day_Period",
                table: "Timetables",
                columns: new[] { "AcademicYearId", "FacultyId", "Day", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Timetables_AcademicYearId_SectionId_Day_Period",
                table: "Timetables",
                columns: new[] { "AcademicYearId", "SectionId", "Day", "Period" },
                unique: true);
        }
    }
}
