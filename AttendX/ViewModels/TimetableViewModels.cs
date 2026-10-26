using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.ViewModels;

public class TimetableFormViewModel
{
    public int? Id { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Select a branch")] public int BranchId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Select a section")] public int SectionId { get; set; }
    [Range(1, 8)] public int Semester { get; set; } = 5;
    public DayOfWeek Day { get; set; } = DayOfWeek.Monday;
    [Range(1, 12)] public int Period { get; set; } = 1;
    [Range(1, int.MaxValue, ErrorMessage = "Select a subject")] public int SubjectId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Select a faculty")] public int FacultyId { get; set; }
    [Required, StringLength(20), Display(Name = "Room")] public string RoomNo { get; set; } = "";
    [Required, StringLength(30), Display(Name = "Class Type")] public string ClassType { get; set; } = "Lecture";
    [Display(Name = "Also assign this faculty to the subject/section if not yet mapped")] public bool AutoMap { get; set; } = true;
    public List<SelectListItem> Branches { get; set; } = [];
    public List<SelectListItem> Sections { get; set; } = [];
    public List<SelectListItem> Subjects { get; set; } = [];
    public List<SelectListItem> Faculty { get; set; } = [];
    public List<SelectListItem> ClassTypes { get; set; } = [new("Lecture", "Lecture"), new("Lab", "Lab"), new("Workshop", "Workshop")];
}

public class TimetableGridViewModel
{
    public int? BranchId { get; set; }
    public int? SectionId { get; set; }
    public int? Semester { get; set; }
    public int? FacultyId { get; set; }
    public bool ReadOnly { get; set; }
    public List<PeriodDefinition> Periods { get; set; } = new();
    public Dictionary<string, List<Timetable>> Cells { get; set; } = new();
    public List<SelectListItem> Branches { get; set; } = new();
    public List<SelectListItem> Sections { get; set; } = new();
    public List<SelectListItem> Faculty { get; set; } = new();
    public bool HasFilter => SectionId != null || FacultyId != null || BranchId != null;
    public List<Timetable> Cell(DayOfWeek d, int period) => Cells.TryGetValue($"{(int)d}-{period}", out var l) ? l : new List<Timetable>();
}
