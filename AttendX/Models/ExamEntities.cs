namespace AttendX.Models;

public class ExamTimetable
{
    public int Id { get; set; }
    public int AcademicYearId { get; set; }
    public AcademicYear? AcademicYear { get; set; }

    public int BranchId { get; set; }
    public Branch? Branch { get; set; }

    public int? SectionId { get; set; }
    public Section? Section { get; set; }

    [Range(1, 8)] public int Semester { get; set; }

    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public int FacultyId { get; set; }
    public Faculty? Faculty { get; set; }

    [Required, StringLength(20)] public string ExamType { get; set; } = "Mid1"; // Mid1 / Mid2 / Final

    public DateTime ExamDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    [StringLength(30)] public string RoomNo { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class ExamMark
{
    public long Id { get; set; }
    public int ExamTimetableId { get; set; }
    public ExamTimetable? ExamTimetable { get; set; }

    public int StudentId { get; set; }
    public Student? Student { get; set; }

    [Range(0, 1000)] public decimal MarksObtained { get; set; }
    [Range(1, 1000)] public decimal MaxMarks { get; set; } = 100;
    public bool IsLocked { get; set; } = false;
    public string? Remarks { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? LastModifiedBy { get; set; }
    public DateTime? LastModifiedAt { get; set; }
}
