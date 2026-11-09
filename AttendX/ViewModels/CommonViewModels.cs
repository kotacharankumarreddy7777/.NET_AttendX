using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.ViewModels;

public class ImportResult
{
    public int Total { get; set; }
    public int Imported { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool HasErrors => Errors.Count > 0;
}

public class PagedList<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalCount { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public class PagerModel
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}

public class ErrorViewModel
{
    public int Code { get; set; } = 500;
    public string Title { get; set; } = "Something went wrong";
    public string Message { get; set; } = "An unexpected error occurred. Please try again.";
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RequestId { get; set; }
    public string? TraceIdentifier { get; set; }
    public string? RequestPath { get; set; }
    public DateTime? TimestampUtc { get; set; }
    public bool HasDetail => !string.IsNullOrWhiteSpace(ErrorType);
    public string DetailSummary => HasDetail
        ? $"{ErrorType}: {ErrorMessage}\n---\nPath: {RequestPath}\nRequestId: {RequestId}\nTraceIdentifier: {TraceIdentifier}\nTime (UTC): {TimestampUtc:O}"
        : "";
}

public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")] public string CurrentPassword { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8), Display(Name = "New password")] public string NewPassword { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword)), Display(Name = "Confirm new password")] public string ConfirmPassword { get; set; } = "";
}

// ---------- Master data forms ----------
public class BranchFormViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, StringLength(15), RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "Code may contain letters, digits and '-' only.")] public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class SectionFormViewModel
{
    public int? Id { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Select a branch")] public int BranchId { get; set; }
    [Required, StringLength(10)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<SelectListItem> Branches { get; set; } = new();
}

public class SubjectFormViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(150), Display(Name = "Subject name")] public string SubjectName { get; set; } = "";
    [Required, StringLength(30), Display(Name = "Subject code")] public string SubjectCode { get; set; } = "";
    [Range(1, int.MaxValue, ErrorMessage = "Select a branch")] public int BranchId { get; set; }
    [Range(1, 8)] public int Semester { get; set; } = 1;
    [Range(0, 12)] public int Credits { get; set; } = 3;
    public bool IsActive { get; set; } = true;
    public List<SelectListItem> Branches { get; set; } = new();
}

public class StudentFormViewModel
{
    public int? Id { get; set; }
    [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(150)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^(\+?\d[\s\-]?){10,15}$", ErrorMessage = "Enter a valid phone number (10-15 digits).")] public string Phone { get; set; } = "";
    [Range(1, int.MaxValue, ErrorMessage = "Select a branch")] public int BranchId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Select a section")] public int SectionId { get; set; }
    [Range(1, 8)] public int Semester { get; set; } = 1;
    [DataType(DataType.Date), Display(Name = "Joining date")] public DateTime JoiningDate { get; set; } = DateTime.Today;
    public IFormFile? Photo { get; set; }
    public string? WebcamPhoto { get; set; }
    public string? ExistingPhoto { get; set; }
    public string? StudentCode { get; set; }
    public List<SelectListItem> Branches { get; set; } = new();
    public List<SelectListItem> Sections { get; set; } = new();
}

public class FacultyFormViewModel
{
    public int? Id { get; set; }
    [StringLength(20), Display(Name = "Faculty ID (auto if empty)")] public string? FacultyID { get; set; }
    [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(150)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^(\+?\d[\s\-]?){10,15}$", ErrorMessage = "Enter a valid phone number.")] public string Phone { get; set; } = "";
    [Range(1, int.MaxValue, ErrorMessage = "Select a department")] public int BranchId { get; set; }
    [Required, StringLength(60)] public string Designation { get; set; } = "Assistant Professor";
    public bool IsValidDesignation => Designations.Contains(Designation, StringComparer.OrdinalIgnoreCase);
    [Display(Name = "Is HOD")] public bool IsHOD { get; set; }
    public List<SelectListItem> Branches { get; set; } = new();
    public static readonly string[] Designations = { "Professor", "Associate Professor", "Assistant Professor", "Lecturer", "Lab Instructor" };
}

public class MappingFormViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a faculty")] public int FacultyId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Select a branch")] public int BranchId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Select a section")] public int SectionId { get; set; }
    [Range(1, 8)] public int Semester { get; set; } = 5;
    [Range(1, int.MaxValue, ErrorMessage = "Select a subject")] public int SubjectId { get; set; }
    public List<SelectListItem> Faculty { get; set; } = new();
    public List<SelectListItem> Branches { get; set; } = new();
    public List<SelectListItem> Sections { get; set; } = new();
    public List<SelectListItem> Subjects { get; set; } = new();
}

public class SettingsViewModel
{
    public AttendanceSettings Settings { get; set; } = new();
    public List<ThresholdRule> Rules { get; set; } = new();
    public List<PeriodDefinition> Periods { get; set; } = new();
    public List<AcademicYear> Years { get; set; } = new();
    public List<SelectListItem> Branches { get; set; } = new();
    public List<SelectListItem> Sections { get; set; } = new();
}
