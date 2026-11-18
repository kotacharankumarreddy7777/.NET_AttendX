namespace AttendX.Controllers;

using AttendX.Data;
using AttendX.Services;

[Authorize(Policy = PolicyNames.AnyStaff)]
public class DashboardController(IDashboardService dash, ICurrentUser user, AppDbContext db) : Controller
{
    private readonly IDashboardService _dash = dash;
    private readonly ICurrentUser _user = user;
    private readonly AppDbContext _db = db;

    /// <summary>One entry point - the user is routed to the dashboard that matches the role.</summary>
    public async Task<IActionResult> Index()
    {
        if (User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.Hod))
            return View("Admin", await _dash.BuildAdminOrHodAsync());
        var vm = await _dash.BuildFacultyAsync();
        if (vm == null) return View("NoFaculty");
        return View("Faculty", vm);
    }

    /// <summary>HOD timetable tab: shows all branch members' timetables for the HOD's scope branch.</summary>
    [Authorize(Policy = PolicyNames.AdminOrHod), HttpGet]
    public async Task<IActionResult> HodTimetable(int? sectionId, int? semester)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        if (scope == null) return Forbid();

        var year = await _db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(a => a.IsCurrent)
            ?? await _db.AcademicYears.AsNoTracking().OrderByDescending(a => a.StartDate).FirstOrDefaultAsync() ?? new AcademicYear { Id = 0, Name = "2026-27" };
        var sem = await _db.Semesters.AsNoTracking().FirstOrDefaultAsync(s => s.IsCurrent);
        var currentSem = semester ?? sem?.Number ?? 0;

        var q = _db.Timetables.AsNoTracking()
            .Include(t => t.Subject).Include(t => t.Section).Include(t => t.Faculty)
            .Where(t => t.IsActive && t.AcademicYearId == year.Id && t.Semester == currentSem && t.BranchId == scope);

        if (sectionId != null) q = q.Where(t => t.SectionId == sectionId);

        var rows = await q.OrderBy(t => t.Faculty!.FullName).ThenBy(t => t.Section!.Name).ThenBy(t => t.Day).ThenBy(t => t.Period).ToListAsync();

        var branches = await _db.Branches.AsNoTracking().Where(b => b.IsActive && b.Id == scope).OrderBy(b => b.Name).ToListAsync();
        var sections = await _db.Sections.AsNoTracking().Where(s => s.IsActive && s.BranchId == scope).OrderBy(s => s.Name).ToListAsync();
        var semesters = await _db.Semesters.AsNoTracking().Where(s => s.AcademicYearId == year.Id).OrderBy(s => s.Number).ToListAsync();

        var byFaculty = rows.GroupBy(r => r.Faculty!.Id).ToDictionary(g => g.Key, g => g.ToList());

        ViewBag.ScopeBranch = branches.FirstOrDefault();
        ViewBag.Sections = sections;
        ViewBag.Semesters = semesters;
        ViewBag.SelectedSection = sectionId;
        ViewBag.SelectedSemester = currentSem;
        ViewBag.CurrentSemester = sem?.Number ?? 0;

        return View(byFaculty);
    }
}
