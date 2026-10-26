namespace AttendX.Models;

public class FacultySubjectMapping
{
    public int Id { get; set; }
    public int FacultyId { get; set; }
    public Faculty? Faculty { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int Semester { get; set; }
    public int AcademicYearId { get; set; }
    public AcademicYear? AcademicYear { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Period timing (Admin configurable). Period 1 = first class of the day.</summary>
public class PeriodDefinition
{
    public int Id { get; set; }
    [Range(1, 12)] public int PeriodNo { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

public class Timetable
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int Semester { get; set; }
    public DayOfWeek Day { get; set; }
    public int Period { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int FacultyId { get; set; }
    public Faculty? Faculty { get; set; }
    [StringLength(20)] public string RoomNo { get; set; } = "";
    [StringLength(30)] public string ClassType { get; set; } = "Lecture";
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsActive { get; set; } = true;
    public int AcademicYearId { get; set; }
    public AcademicYear? AcademicYear { get; set; }
}
