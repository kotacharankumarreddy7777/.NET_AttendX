namespace AttendX.Models;

public class AcademicYear
{
    public int Id { get; set; }
    [Required, StringLength(9)] public string Name { get; set; } = "";   // e.g. 2026-27
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsCurrent { get; set; }
}

/// <summary>
/// One semester of an academic year. Odd semesters (1,3,5,7) run Aug–Dec, even semesters (2,4,6,8) run Jan–May.
/// Used by the academic calendar, timetable generation, exam scheduling and attendance history seeding.
/// </summary>
public class Semester
{
    public int Id { get; set; }
    public int AcademicYearId { get; set; }
    public AcademicYear? AcademicYear { get; set; }
    /// <summary>1..8 (odd = 1,3,5,7 ; even = 2,4,6,8).</summary>
    [Range(1, 8)] public int Number { get; set; }
    /// <summary>Odd / Even.</summary>
    [Required, StringLength(10)] public string Type { get; set; } = "Odd";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    /// <summary>Last teaching day (before end-sem exams).</summary>
    public DateTime ClassesEndDate { get; set; }
    /// <summary>End-semester exam start.</summary>
    public DateTime ExamStartDate { get; set; }
    /// <summary>End-semester exam end.</summary>
    public DateTime ExamEndDate { get; set; }
    public bool IsCurrent { get; set; }
}

public class Branch
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(15)] public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public ICollection<Section> Sections { get; set; } = new List<Section>();
}

public class Section
{
    public int Id { get; set; }
    [Required, StringLength(10)] public string Name { get; set; } = "";
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Subject
{
    public int Id { get; set; }   // SubjectID
    [Required, StringLength(150)] public string SubjectName { get; set; } = "";
    [Required, StringLength(30)] public string SubjectCode { get; set; } = "";
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    [Range(1, 8)] public int Semester { get; set; }
    [Range(0, 12)] public int Credits { get; set; }
    public bool IsActive { get; set; } = true;
}
