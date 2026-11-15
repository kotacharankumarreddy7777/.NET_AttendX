namespace AttendX.Models;

/// <summary>Exam definition: Mid-Sem 1, Mid-Sem 2, End-Sem, Remedial.</summary>
public class Exam
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";      // e.g. "Mid-Semester 1", "End-Semester"
    [Required, StringLength(20)] public string Type { get; set; } = "Internal"; // Internal / External / Practical
    public int Semester { get; set; }                                          // 1-8
    public int AcademicYearId { get; set; }
    public AcademicYear? AcademicYear { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Exam timetable slot mapping subject, faculty invigilator, room, date/time.</summary>
public class ExamTimetable
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int Semester { get; set; }
    public int FacultyId { get; set; }                                         // Invigilator
    public Faculty? Faculty { get; set; }
    public DateTime ExamDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    [StringLength(20)] public string RoomNo { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

/// <summary>Student registration for an exam - linked to attendance eligibility gate.</summary>
public class ExamRegistration
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public bool IsEligible { get; set; }                                       // Based on attendance >= threshold
    public string? EligibilityReason { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public bool HallTicketGenerated { get; set; } = false;
}

/// <summary>Exam marks for a student in a subject, with faculty lock + modification query workflow.</summary>
public class ExamMark
{
    public int Id { get; set; }
    public int ExamTimetableId { get; set; }
    public ExamTimetable? ExamTimetable { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    [Range(0, 100)] public decimal InternalMark { get; set; }                  // 30 marks
    [Range(0, 100)] public decimal ExternalMark { get; set; }                  // 70 marks
    public decimal TotalMark => InternalMark + ExternalMark;
    [StringLength(5)] public string? Grade { get; set; }
    [StringLength(500)] public string? Remarks { get; set; }
    [StringLength(450)] public string? MarkedBy { get; set; }
    public DateTime MarkedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastModifiedAt { get; set; }

    // ---- Lock workflow (faculty locks after external exam; HOD/Admin can allot modification window) ----
    /// <summary>Draft = faculty editing; Submitted = faculty submitted for review; Locked = HOD/Admin locked (no edits).</summary>
    [StringLength(20)] public string LockStatus { get; set; } = "Draft";
    public DateTime? LockedAt { get; set; }
    [StringLength(450)] public string? LockedBy { get; set; }

    /// <summary>If faculty needs to modify after lock, they raise a query. HOD/Admin approves and allots a time window.</summary>
    [StringLength(20)] public string ModificationQueryStatus { get; set; } = "None";   // None / Pending / Approved / Rejected
    [StringLength(500)] public string? ModificationQueryReason { get; set; }
    [StringLength(450)] public string? ModificationQueryBy { get; set; }
    public DateTime? ModificationQueryAt { get; set; }
    [StringLength(450)] public string? ModificationReviewedBy { get; set; }
    public DateTime? ModificationReviewedAt { get; set; }
    [StringLength(500)] public string? ModificationReviewNote { get; set; }
    /// <summary>When approved, faculty can modify only within this window.</summary>
    public DateTime? ModificationWindowStart { get; set; }
    public DateTime? ModificationWindowEnd { get; set; }
}

