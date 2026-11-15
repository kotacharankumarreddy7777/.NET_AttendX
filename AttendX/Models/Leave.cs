namespace AttendX.Models;

/// <summary>Type of leave: Medical, Casual, On-Duty, etc.</summary>
public class LeaveType
{
    public int Id { get; set; }
    [Required, StringLength(50)] public string Name { get; set; } = "";   // Medical, Casual, On-Duty, Maternity, Paternity
    [Range(0, 365)] public int DefaultDaysAllowed { get; set; } = 12;
    public bool IsActive { get; set; } = true;
}

/// <summary>Leave balance per student per academic year.</summary>
public class LeaveBalance
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int AcademicYearId { get; set; }
    public AcademicYear? AcademicYear { get; set; }
    [Range(0, 365)] public int TotalDays { get; set; }
    [Range(0, 365)] public int UsedDays { get; set; }
    public int RemainingDays => TotalDays - UsedDays;
}

/// <summary>Leave application submitted by student/parent.</summary>
public class LeaveApplication
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalDays => (int)(EndDate - StartDate).TotalDays + 1;
    [Required, StringLength(500)] public string Reason { get; set; } = "";
    /// <summary>Pending / Approved / Rejected</summary>
    [Required, StringLength(20)] public string Status { get; set; } = "Pending";
    [StringLength(500)] public string? AdminResponse { get; set; }
    [StringLength(450)] public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Parent linked to a student for the parent portal.</summary>
public class Parent
{
    public int Id { get; set; }
    [Required, StringLength(120)] public string FullName { get; set; } = "";
    [Required, StringLength(150), EmailAddress] public string Email { get; set; } = "";
    [Required, StringLength(15)] public string Phone { get; set; } = "";
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public string? IdentityUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

