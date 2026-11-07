using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.ViewModels;

public class StudentSubjectRow
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string FacultyName { get; set; } = "";
    public int Total { get; set; }
    public int Present { get; set; }
    public int Late { get; set; }
    public int Absent { get; set; }
    public decimal Percent { get; set; }
}

/// <summary>One cell of the 5-week heat map. Css: p = present, l = late/mixed, a = absent, n = no class, h = holiday, s = sunday.</summary>
public class HeatDay
{
    public DateTime Date { get; set; }
    public string Css { get; set; } = "n";
    public string Tip { get; set; } = "";
}

public class TimetableCellVm
{
    public string SubjectCode { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string Faculty { get; set; } = "";
    public string Room { get; set; } = "";
}

public class FacultyContact
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
}

public class StudentDashboardViewModel
{
    public Student Student { get; set; } = null!;
    public string BranchName { get; set; } = "";
    public string SectionName { get; set; } = "";
    public int ClassStrength { get; set; }
    public int Total { get; set; }
    public int Present { get; set; }
    public int Late { get; set; }
    public int Absent { get; set; }
    public decimal Percent { get; set; }
    public decimal Threshold { get; set; }
    public decimal Projected { get; set; }
    public int ClassesNeeded { get; set; }
    public string Risk { get; set; } = "Safe";
    public string Advice { get; set; } = "";
    public List<StudentSubjectRow> Subjects { get; set; } = new();
    public List<HeatDay> Heat { get; set; } = new();
    public string? NonWorkingReason { get; set; }
    public List<SlotView> TodayClasses { get; set; } = new();
    public List<PeriodDefinition> Periods { get; set; } = new();
    public Dictionary<string, TimetableCellVm> Week { get; set; } = new();
    public List<Attendance> Recent { get; set; } = new();
    public List<FacultyContact> Faculty { get; set; } = new();
    public List<Notice> Notices { get; set; } = new();
    public List<Holiday> Holidays { get; set; } = new();
    public List<AttendanceQuery> Queries { get; set; } = new();
    public List<AuditLog> Logins { get; set; } = new();
    public DateTime? LastLogin { get; set; }
    public List<SelectListItem> SubjectOptions { get; set; } = new();
    public string? NextClassText { get; set; }
}

public class FacultyClassChip
{
    public string Label { get; set; } = "";
    public int Strength { get; set; }
    public decimal AvgPercent { get; set; }
}

public class FacultyHomeExtras
{
    public string? NonWorkingReason { get; set; }
    public int ClassesThisWeek { get; set; }
    public int StudentsTaught { get; set; }
    public decimal AvgAttendance { get; set; }
    public List<FacultyClassChip> Chips { get; set; } = new();
    public List<StudentSummary> LowStudents { get; set; } = new();
    public List<Notice> Notices { get; set; } = new();
    public List<Holiday> Holidays { get; set; } = new();
    public List<SlotView> Missing { get; set; } = new();
    public DateTime? LastLogin { get; set; }
    public int OpenQueries { get; set; }
    public Dictionary<string, TimetableCellVm> Week { get; set; } = new();
    public List<PeriodDefinition> Periods { get; set; } = new();
}

public class WeekGridModel
{
    public List<PeriodDefinition> Periods { get; set; } = new();
    public Dictionary<string, TimetableCellVm> Week { get; set; } = new();
    public string Caption { get; set; } = "Faculty";     // what the 2nd line of a cell shows (faculty name for students, class for faculty)
}

public record EmptyStateModel(string Key, string Title, string Message, string? ActionText = null, string? ActionUrl = null);

public class RoleLoginViewModel
{
    public string Role { get; set; } = "student";
    [StringLength(40), Display(Name = "Institution ID")] public string? InstitutionId { get; set; }
    [StringLength(150)] public string? Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    [Display(Name = "Remember me")] public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class NoticeFormViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(150)] public string Title { get; set; } = "";
    [Required, StringLength(2000)] public string Body { get; set; } = "";
    [Required] public string Audience { get; set; } = "All";
    public int? BranchId { get; set; }
    public int? SectionId { get; set; }
    [DataType(DataType.Date)] public DateTime? ExpiresAt { get; set; }
    public bool IsPinned { get; set; }
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Branches { get; set; } = new();
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Sections { get; set; } = new();
}
