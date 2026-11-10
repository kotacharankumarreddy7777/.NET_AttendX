using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AnyStaff)]
public class TimetableController(AppDbContext db, ITimetableService tt, ICurrentUser user) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ITimetableService _tt = tt;
    private readonly ICurrentUser _user = user;

    public async Task<IActionResult> Index(int? branchId, int? sectionId, int? semester, int? facultyId)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        bool facultyOnly = !_user.IsAdmin && !_user.IsHod;
        if (facultyOnly) { var me = await _user.GetFacultyAsync(); facultyId = me?.Id ?? -1; }
        else if (scope.HasValue) branchId = scope;

        var vm = await _tt.BuildGridAsync(branchId, sectionId, semester, facultyId, scope);
        vm.ReadOnly = !_user.IsAdmin;
        vm.Branches = await _db.Branches.AsNoTracking().Where(b => b.IsActive && (!scope.HasValue || b.Id == scope)).OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToListAsync();
        if (branchId != null) vm.Sections = await _db.Sections.AsNoTracking().Where(s => s.BranchId == branchId && s.IsActive).OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
        if (!facultyOnly) vm.Faculty = await _db.Faculty.AsNoTracking().Where(f => f.IsActive && (!scope.HasValue || f.BranchId == scope)).OrderBy(f => f.FullName).Select(f => new SelectListItem(f.FullName, f.Id.ToString())).ToListAsync();
        ViewBag.FacultyOnly = facultyOnly;
        return View(vm);
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Create(int? branchId, int? sectionId, int? semester, DayOfWeek? day, int? period)
        => View("Form", await Fill(new TimetableFormViewModel { BranchId = branchId ?? 0, SectionId = sectionId ?? 0, Semester = semester ?? 5, Day = day ?? DayOfWeek.Monday, Period = period ?? 1 }));

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Edit(int id)
    {
        var t = await _db.Timetables.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        return View("Form", await Fill(new TimetableFormViewModel { Id = t.Id, BranchId = t.BranchId, SectionId = t.SectionId, Semester = t.Semester, Day = t.Day, Period = t.Period, SubjectId = t.SubjectId, FacultyId = t.FacultyId, RoomNo = t.RoomNo, ClassType = t.ClassType, AutoMap = true }));
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Save(TimetableFormViewModel m)
    {
        if (ModelState.IsValid)
        {
            var (ok, errors) = await _tt.SaveAsync(m);
            if (ok) { TempData["Success"] = "Timetable slot saved."; return RedirectToAction(nameof(Index), new { branchId = m.BranchId, sectionId = m.SectionId, semester = m.Semester }); }
            foreach (var e in errors) ModelState.AddModelError("", e);
        }
        return View("Form", await Fill(m));
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Remove(int id, int? sectionId, int? branchId, int? semester)
    {
        var ok = await _tt.DeactivateAsync(id);
        if (!ok) TempData["Error"] = "That slot no longer exists.";
        else TempData["Success"] = "Slot removed (history kept).";
        return RedirectToAction(nameof(Index), new { branchId, sectionId, semester });
    }

    private async Task<TimetableFormViewModel> Fill(TimetableFormViewModel m)
    {
        m.Branches = await _db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToListAsync();
        if (m.BranchId > 0)
        {
            m.Sections = await _db.Sections.AsNoTracking().Where(s => s.BranchId == m.BranchId && s.IsActive).OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
            m.Subjects = await _db.Subjects.AsNoTracking().Where(s => s.BranchId == m.BranchId && s.Semester == m.Semester && s.IsActive).OrderBy(s => s.SubjectCode).Select(s => new SelectListItem(s.SubjectCode + " - " + s.SubjectName, s.Id.ToString())).ToListAsync();
        }
        m.Faculty = await _db.Faculty.AsNoTracking().Where(f => f.IsActive).OrderBy(f => f.FullName).Select(f => new SelectListItem(f.FullName + " (" + f.FacultyID + ")", f.Id.ToString())).ToListAsync();
        return m;
    }
}
