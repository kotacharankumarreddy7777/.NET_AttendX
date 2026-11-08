using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AnyStaff)]
public class SectionsController(AppDbContext db, IAuditService audit) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly IAuditService _audit = audit;

    /// <summary>Used by every cascading Branch -> Section dropdown.</summary>
    [HttpGet]
    public async Task<IActionResult> ByBranch(int branchId, bool includeInactive = false)
    {
        var q = _db.Sections.AsNoTracking().Where(s => s.BranchId == branchId);
        if (!includeInactive) q = q.Where(s => s.IsActive);
        return Json(await q.OrderBy(s => s.Name).Select(s => new { id = s.Id, name = s.Name }).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> SubjectsFor(int branchId, int semester)
        => Json(await _db.Subjects.AsNoTracking().Where(s => s.IsActive && s.BranchId == branchId && s.Semester == semester).OrderBy(s => s.SubjectCode)
            .Select(s => new { id = s.Id, name = s.SubjectCode + " - " + s.SubjectName }).ToListAsync());

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Index(int? branchId)
    {
        ViewBag.Branches = await Branches();
        ViewBag.BranchId = branchId;
        var q = _db.Sections.AsNoTracking().Include(s => s.Branch).AsQueryable();
        if (branchId != null) q = q.Where(s => s.BranchId == branchId);
        var counts = await _db.Students.Where(s => s.IsActive).GroupBy(s => s.SectionId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        ViewBag.Counts = counts;
        return View(await q.OrderBy(s => s.Branch!.Code).ThenBy(s => s.Name).ToListAsync());
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Create() => View("Form", new SectionFormViewModel { Branches = await Branches() });

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Edit(int id)
    {
        var s = await _db.Sections.FindAsync(id);
        return s == null ? NotFound() : View("Form", new SectionFormViewModel { Id = s.Id, BranchId = s.BranchId, Name = s.Name, IsActive = s.IsActive, Branches = await Branches() });
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Save(SectionFormViewModel m)
    {
        m.Branches = await Branches();
        if (!ModelState.IsValid) return View("Form", m);
        if (!await _db.Branches.AnyAsync(b => b.Id == m.BranchId && b.IsActive)) { ModelState.AddModelError(nameof(m.BranchId), "Selected branch does not exist."); return View("Form", m); }
        var name = m.Name.Trim().ToUpperInvariant();
        if (await _db.Sections.AnyAsync(s => s.BranchId == m.BranchId && s.Name == name && s.Id != (m.Id ?? 0)))
        { ModelState.AddModelError("", "This section already exists in the branch."); return View("Form", m); }
        try
        {
            Section s;
            if (m.Id.HasValue) s = (await _db.Sections.FindAsync(m.Id.Value))!;
            else { s = new Section(); _db.Sections.Add(s); }
            s.BranchId = m.BranchId; s.Name = name; s.IsActive = m.IsActive;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(m.Id.HasValue ? "SectionUpdated" : "SectionCreated", "Section", s.Name, branchId: s.BranchId);
            TempData["Success"] = "Section saved.";
            return RedirectToAction(nameof(Index), new { branchId = m.BranchId });
        }
        catch (DbUpdateException) { ModelState.AddModelError("", "This section already exists in the branch."); return View("Form", m); }
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Toggle(int id)
    {
        var s = await _db.Sections.FindAsync(id);
        if (s == null) return NotFound();
        s.IsActive = !s.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(s.IsActive ? "SectionActivated" : "SectionDeactivated", "Section", s.Name, branchId: s.BranchId);
        return RedirectToAction(nameof(Index), new { branchId = s.BranchId });
    }

    private async Task<List<SelectListItem>> Branches()
        => await _db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name + " (" + b.Code + ")", b.Id.ToString())).ToListAsync();
}
