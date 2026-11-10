using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AnyStaff)]
public class ReportsController(IReportService reports, IExportService export, AppDbContext db, ICurrentUser user) : Controller
{
    private readonly IReportService _reports = reports;
    private readonly IExportService _export = export;
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private static readonly (string Value, string Label)[] Types =
    {
        ("section", "Section-wise"), ("subject", "Subject-wise"), ("student", "Student-wise (by subject)"), ("faculty", "Faculty activity"),
        ("daily", "Daily"), ("monthly", "Monthly"), ("range", "Date range (per student)"), ("low", "Low attendance"), ("defaulter", "Defaulters")
    };

    public async Task<IActionResult> Index(ReportFilter f, bool run = false)
    {
        if (string.IsNullOrWhiteSpace(f.Type)) f.Type = "section";
        if (!Types.Any(t => t.Value == f.Type))
        {
            ModelState.AddModelError("Type", "Unknown report type.");
        }
        var vm = await Build(f);
        if (!ModelState.IsValid) return View(vm);
        if (run) vm.Table = await _reports.BuildAsync(f);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(ReportFilter f, string format = "excel")
    {
        if (string.IsNullOrWhiteSpace(f.Type)) f.Type = "section";
        if (!Types.Any(t => t.Value == f.Type)) return BadRequest("Unknown report type.");
        var table = await _reports.BuildAsync(f);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        var name = $"AttendX_{f.Type}_{stamp}";
        return format.ToLowerInvariant() switch
        {
            "csv" => File(_export.ToCsv(table), "text/csv", name + ".csv"),
            "pdf" => File(_export.ToPdf(table), "application/pdf", name + ".pdf"),
            _ => File(_export.ToExcel(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx")
        };
    }

    private async Task<ReportPageViewModel> Build(ReportFilter f)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        bool locked = scope.HasValue && !_user.IsAdmin;      // HOD (and Faculty, whose scope is null) get branch-restricted views
        if (locked) f.BranchId = scope;
        var vm = new ReportPageViewModel { Filter = f, BranchLocked = locked };
        vm.Types = [.. Types.Select(t => new SelectListItem(t.Label, t.Value, t.Value == f.Type))];
        vm.Branches = await _db.Branches.AsNoTracking().Where(b => b.IsActive && (!locked || b.Id == scope)).OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToListAsync();
        if (f.BranchId != null)
        {
            vm.Sections = await _db.Sections.AsNoTracking().Where(s => s.BranchId == f.BranchId && s.IsActive).OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
            if (f.Semester != null)
                vm.Subjects = await _db.Subjects.AsNoTracking().Where(s => s.BranchId == f.BranchId && s.Semester == f.Semester).OrderBy(s => s.SubjectCode).Select(s => new SelectListItem(s.SubjectCode + " - " + s.SubjectName, s.Id.ToString())).ToListAsync();
        }
        if (_user.IsAdmin || _user.IsHod)
            vm.Faculty = await _db.Faculty.AsNoTracking().Where(x => x.IsActive && (!locked || x.BranchId == scope)).OrderBy(x => x.FullName).Select(x => new SelectListItem(x.FullName, x.Id.ToString())).ToListAsync();
        return vm;
    }
}
