using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendX.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExamUniqueIndexFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Exams_AcademicYearId_Semester_Type",
                table: "Exams");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_AcademicYearId_Semester_Name",
                table: "Exams",
                columns: new[] { "AcademicYearId", "Semester", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Exams_AcademicYearId_Semester_Name",
                table: "Exams");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_AcademicYearId_Semester_Type",
                table: "Exams",
                columns: new[] { "AcademicYearId", "Semester", "Type" },
                unique: true);
        }
    }
}
