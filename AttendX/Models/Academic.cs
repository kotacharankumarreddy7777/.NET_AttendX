namespace AttendX.Models;

public class AcademicYear
{
    public int Id { get; set; }
    [Required, StringLength(9)] public string Name { get; set; } = "";   // e.g. 2026-27
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
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
