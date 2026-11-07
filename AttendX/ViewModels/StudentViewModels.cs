using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.ViewModels;

public class StudentListViewModel
{
    public string? Search { get; set; }
    public int? BranchId { get; set; }
    public int? SectionId { get; set; }
    public int? Semester { get; set; }
    public int? Year { get; set; }
    public string Status { get; set; } = "active";    // active / inactive / all
    public PagedList<Student> Page { get; set; } = new();
    public List<SelectListItem> Branches { get; set; } = new();
    public List<SelectListItem> Sections { get; set; } = new();
}

public class SubjectPercent
{
    public string Subject { get; set; } = "";
    public int Total { get; set; }
    public int Present { get; set; }
    public int Late { get; set; }
    public int Absent { get; set; }
    public decimal Percent { get; set; }
}

public class StudentDetailsViewModel
{
    public Student Student { get; set; } = null!;
    public decimal OverallPercent { get; set; }
    public decimal Threshold { get; set; }
    public string Risk { get; set; } = "";
    public int Total { get; set; }
    public List<SubjectPercent> Subjects { get; set; } = new();
    public List<Attendance> Recent { get; set; } = new();
}
