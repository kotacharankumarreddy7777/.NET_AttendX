using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendX.Data.Migrations
{
    /// <inheritdoc />
    public partial class TimetableRoomUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Timetables_AcademicYearId_RoomNo_Day_Period_Semester",
                table: "Timetables",
                columns: new[] { "AcademicYearId", "RoomNo", "Day", "Period", "Semester" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Timetables_AcademicYearId_RoomNo_Day_Period_Semester",
                table: "Timetables");
        }
    }
}
