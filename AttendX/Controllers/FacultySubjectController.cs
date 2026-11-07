using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AdminOrHod)]
public class FacultySubjectController(AppDbContext db, ICurrentUser user, ISettingsService settings, IAuditService audit) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly ISettingsService _settings = settings;
    private readonly IAuditService _audit = audit;

    public async Task<IActionResult> Index(int? facultyId, int? branchId)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        if (scope != null) branchId = scope;
        var year = await _settings.GetCurrentYearAsync();
        var q = _db.FacultySubjectMappings.AsNoTracking().Include(m => m.Faculty).Include(m => m.Subject).Include(m => m.Section).Include(m => m.Branch)
            .Where(m => m.AcademicYearId == year.Id);
        if (facultyId != null) q = q.Where(m => m.FacultyId == facultyId);
        if (branchId != null) q = q.Where(m => m.BranchId == branchId);
        ViewBag.Faculty = await FacultyItems(scope);
        ViewBag.FacultyId = facultyId; ViewBag.Year = year.Name;
        return View(await q.OrderBy(m => m.Faculty!.FullName).ThenBy(m => m.Subject!.SubjectCode).Take(600).ToListAsync());
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Create() => View(await Fill(new MappingFormViewModel()));

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Create(MappingFormViewModel m)
    {
        if (ModelState.IsValid)
        {
            var year = await _settings.GetCurrentYearAsync();
            var section = await _db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == m.SectionId);
            var subject = await _db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == m.SubjectId);
            var faculty = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(f => f.Id == m.FacultyId && f.IsActive);
            if (section == null || subject == null || faculty == null) ModelState.AddModelError("", "Invalid faculty, section or subject.");
            else if (section.BranchId != m.BranchId || subject.BranchId != m.BranchId) ModelState.AddModelError("", "Section and subject must belong to the selected branch.");
            else if (subject.Semester != m.Semester) ModelState.AddModelError("", $"Subject {subject.SubjectCode} belongs to semester {subject.Semester}, not {m.Semester}.");
            else
            {
                var existing = await _db.FacultySubjectMappings.FirstOrDefaultAsync(x => x.FacultyId == m.FacultyId && x.SubjectId == m.SubjectId && x.SectionId == m.SectionId && x.AcademicYearId == year.Id);
                if (existing != null && existing.Active) ModelState.AddModelError("", "This mapping already exists.");
                else
                {
                    if (existing != null) existing.Active = true;
                    else _db.FacultySubjectMappings.Add(new FacultySubjectMapping { FacultyId = m.FacultyId, SubjectId = m.SubjectId, BranchId = m.BranchId, SectionId = m.SectionId, Semester = m.Semester, AcademicYearId = year.Id });
                    await _db.SaveChangesAsync();
                    await _audit.LogAsync("MappingCreated", "FacultySubject", $"{faculty.FacultyID}/{subject.SubjectCode}/{section.Name}", branchId: m.BranchId);
                    TempData["Success"] = "Faculty assigned.";
                    return RedirectToAction(nameof(Index), new { facultyId = m.FacultyId });
                }
            }
        }
        return View(await Fill(m));
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Remove(int id)
    {
        var m = await _db.FacultySubjectMappings.FindAsync(id);
        if (m == null) return NotFound();
        // mappings used by an active timetable slot cannot be removed
        bool inUse = await _db.Timetables.AnyAsync(t => t.IsActive && t.FacultyId == m.FacultyId && t.SubjectId == m.SubjectId && t.SectionId == m.SectionId && t.AcademicYearId == m.AcademicYearId);
        if (inUse) { TempData["Error"] = "This assignment is used by active timetable slots. Remove those slots first."; return RedirectToAction(nameof(Index)); }
        m.Active = false;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("MappingRemoved", "FacultySubject", id.ToString(), branchId: m.BranchId);
        return RedirectToAction(nameof(Index), new { facultyId = m.FacultyId });
    }

    private async Task<List<SelectListItem>> FacultyItems(int? scope)
    {
        var q = _db.Faculty.AsNoTracking().Where(f => f.IsActive);
        if (scope != null) q = q.Where(f => f.BranchId == scope);
        return await q.OrderBy(f => f.FullName).Select(f => new SelectListItem(f.FullName + " (" + f.FacultyID + ")", f.Id.ToString())).ToListAsync();
    }

    private async Task<MappingFormViewModel> Fill(MappingFormViewModel m)
    {
        m.Faculty = await FacultyItems(null);
        m.Branches = await _db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToListAsync();
        if (m.BranchId > 0)
        {
            m.Sections = await _db.Sections.AsNoTracking().Where(s => s.BranchId == m.BranchId && s.IsActive).OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
            m.Subjects = await _db.Subjects.AsNoTracking().Where(s => s.BranchId == m.BranchId && s.Semester == m.Semester && s.IsActive).OrderBy(s => s.SubjectCode)
                .Select(s => new SelectListItem(s.SubjectCode + " - " + s.SubjectName, s.Id.ToString())).ToListAsync();
        }
        return m;
    }
}
