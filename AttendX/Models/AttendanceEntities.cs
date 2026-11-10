namespace AttendX.Models;

public enum AttendanceStatus { Present = 1, Absent = 2, Late = 3 }

/// <summary>One class (timetable slot) on one date. Everything marked in that class hangs off this session.</summary>
public class AttendanceSession
{
    public int Id { get; set; }
    public int TimetableId { get; set; }
    public Timetable? Timetable { get; set; }
    public int AcademicYearId { get; set; }
    public AcademicYear? AcademicYear { get; set; }
    public DateTime Date { get; set; }
    public int Period { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int FacultyId { get; set; }
    public Faculty? Faculty { get; set; }
    [StringLength(20)] public string Mode { get; set; } = "Manual";      // Manual / QR
    [StringLength(64)] public string? DeviceId { get; set; }
    /// <summary>Optional teacher location, rounded to 3 decimals (~100 m) to minimise personal data.</summary>
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    [StringLength(450)] public string? StartedByUserId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
}

public class Attendance
{
    public long Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public int FacultyId { get; set; }
    public Faculty? Faculty { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    /// <summary>Copied from the student at marking time so history is never mixed when a student is promoted.</summary>
    public int BranchId { get; set; }
    public int Semester { get; set; }
    public DateTime Date { get; set; }
    public int Period { get; set; }
    public AttendanceStatus Status { get; set; }
    public DateTime MarkedAt { get; set; } = DateTime.UtcNow;
    public int AttendanceSessionId { get; set; }
    public AttendanceSession? AttendanceSession { get; set; }
    [StringLength(20)] public string Source { get; set; } = "Manual";   // Manual / QR / Offline
    [StringLength(64)] public string? DeviceId { get; set; }
    public DateTime? LastModifiedAt { get; set; }
    [StringLength(150)] public string? LastModifiedBy { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
