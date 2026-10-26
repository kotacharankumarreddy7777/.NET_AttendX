using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.ViewModels;

public class ReportFilter
{
    public string Type { get; set; } = "section";
    public int? BranchId { get; set; }
    public int? SectionId { get; set; }
    public int? Semester { get; set; }
    public int? SubjectId { get; set; }
    public int? FacultyId { get; set; }
    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }
}

/// <summary>Generic tabular report - the same object feeds the HTML view, CSV, Excel and PDF exports.</summary>
public class ReportTable
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public List<string> Columns { get; set; } = new();
    public List<string[]> Rows { get; set; } = new();
    public string[]? Footer { get; set; }
    public int PercentColumn { get; set; } = -1;
    public decimal Threshold { get; set; } = 75;
}

public class ReportPageViewModel
{
    public ReportFilter Filter { get; set; } = new();
    public ReportTable? Table { get; set; }
    public List<SelectListItem> Types { get; set; } = new();
    public List<SelectListItem> Branches { get; set; } = new();
    public List<SelectListItem> Sections { get; set; } = new();
    public List<SelectListItem> Subjects { get; set; } = new();
    public List<SelectListItem> Faculty { get; set; } = new();
    public bool BranchLocked { get; set; }
}

public class StudentSummary
{
    public int StudentId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Section { get; set; } = "";
    public int Semester { get; set; }
    public int BranchId { get; set; }
    public int SectionId { get; set; }
    public int Total { get; set; }
    public int Present { get; set; }
    public int Late { get; set; }
    public int Absent { get; set; }
    public decimal Percent { get; set; }
    public decimal Threshold { get; set; }
    public string Risk { get; set; } = "";
    public int ClassesNeeded { get; set; }
}
