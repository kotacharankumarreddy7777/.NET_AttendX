namespace AttendX.Models;

public class Student
{
    public int Id { get; set; }
    /// <summary>Auto generated: [Semester][Section]-[BranchCode]-[001], e.g. 3EC2-ECE-014</summary>
    [Required, StringLength(30)] public string StudentID { get; set; } = "";
    [Required, StringLength(120)] public string FullName { get; set; } = "";
    [Required, StringLength(150), EmailAddress] public string Email { get; set; } = "";
    [Required, StringLength(15)] public string Phone { get; set; } = "";
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    [Range(1, 8)] public int Semester { get; set; }
    /// <summary>Always calculated from Semester (1-2=>1, 3-4=>2, 5-6=>3, 7-8=>4).</summary>
    public int Year { get; set; }
    [StringLength(200)] public string? PhotoPath { get; set; }
    [StringLength(200)] public string? QRPath { get; set; }
    /// <summary>Login account of this student (Identity user). Students sign in with Institution ID (StudentID) + email + password.</summary>
    [StringLength(450)] public string? IdentityUserId { get; set; }
    public DateTime JoiningDate { get; set; } = DateTime.UtcNow.Date;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class Faculty
{
    public int Id { get; set; }
    [Required, StringLength(20)] public string FacultyID { get; set; } = "";
    [Required, StringLength(120)] public string FullName { get; set; } = "";
    [Required, StringLength(150), EmailAddress] public string Email { get; set; } = "";
    [Required, StringLength(15)] public string Phone { get; set; } = "";
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
    [StringLength(100)] public string Department { get; set; } = "";
    [Required, StringLength(60)] public string Designation { get; set; } = "";
    public bool IsHOD { get; set; }
    public bool IsActive { get; set; } = true;
    [StringLength(450)] public string? IdentityUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
