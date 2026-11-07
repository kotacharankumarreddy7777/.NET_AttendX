namespace AttendX.Models;

/// <summary>Non-working days. Sundays are automatic; this table holds holidays, exams and events.</summary>
public class Holiday
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    [Required, StringLength(120)] public string Name { get; set; } = "";
    /// <summary>Holiday (blocks attendance) / Event / Exam (information only)</summary>
    [Required, StringLength(20)] public string Type { get; set; } = "Holiday";
}

public class Notice
{
    public int Id { get; set; }
    [Required, StringLength(150)] public string Title { get; set; } = "";
    [Required, StringLength(2000)] public string Body { get; set; } = "";
    /// <summary>All / Faculty / Student / Branch / Section</summary>
    [Required, StringLength(20)] public string Audience { get; set; } = "All";
    public int? BranchId { get; set; }
    public int? SectionId { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.Now;
    public DateTime? ExpiresAt { get; set; }
    public bool IsPinned { get; set; }
    [StringLength(150)] public string CreatedBy { get; set; } = "";
}

/// <summary>A student asks faculty/admin to re-check an attendance record.</summary>
public class AttendanceQuery
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int BranchId { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int? FacultyId { get; set; }
    public DateTime RecordDate { get; set; }
    [Required, StringLength(500)] public string Message { get; set; } = "";
    /// <summary>Open / Resolved / Rejected</summary>
    [StringLength(15)] public string Status { get; set; } = "Open";
    [StringLength(500)] public string? Response { get; set; }
    [StringLength(150)] public string? ResponderName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? RespondedAt { get; set; }
}
