using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AdminOrHod)]
public class SubjectsController(AppDbContext db, ICurrentUser user, IAuditService audit) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly IAuditService _audit = audit;

    public async Task<IActionResult> Index(int? branchId, int? semester, string? search)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        if (scope != null) branchId = scope;
        var q = _db.Subjects.AsNoTracking().Include(s => s.Branch).AsQueryable();
        if (branchId != null) q = q.Where(s => s.BranchId == branchId);
        if (semester != null) q = q.Where(s => s.Semester == semester);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(s => s.SubjectName.Contains(search) || s.SubjectCode.Contains(search));
        ViewBag.Branches = await Branches(scope);
        ViewBag.BranchId = branchId; ViewBag.Semester = semester; ViewBag.Search = search; ViewBag.Locked = scope != null;
        return View(await q.OrderBy(s => s.Branch!.Code).ThenBy(s => s.Semester).ThenBy(s => s.SubjectCode).Take(500).ToListAsync());
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Create() => View("Form", new SubjectFormViewModel { Branches = await Branches(null) });

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Edit(int id)
    {
        var s = await _db.Subjects.FindAsync(id);
        if (s == null) return NotFound();
        return View("Form", new SubjectFormViewModel { Id = s.Id, SubjectName = s.SubjectName, SubjectCode = s.SubjectCode, BranchId = s.BranchId, Semester = s.Semester, Credits = s.Credits, IsActive = s.IsActive, Branches = await Branches(null) });
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Save(SubjectFormViewModel m)
    {
        m.Branches = await Branches(null);
        if (!ModelState.IsValid) return View("Form", m);
        if (!await _db.Branches.AnyAsync(b => b.Id == m.BranchId && b.IsActive)) { ModelState.AddModelError(nameof(m.BranchId), "Selected branch does not exist."); return View("Form", m); }
        var code = (m.SubjectCode ?? "").Trim().ToUpperInvariant();
        if (await _db.Subjects.AnyAsync(s => s.SubjectCode.ToUpper() == code && s.Id != (m.Id ?? 0)))
        { ModelState.AddModelError("", "This subject code already exists."); return View("Form", m); }
        Subject s;
        if (m.Id.HasValue) s = (await _db.Subjects.FindAsync(m.Id.Value))!;
        else { s = new Subject(); _db.Subjects.Add(s); }
        s.SubjectName = m.SubjectName.Trim(); s.SubjectCode = code; s.BranchId = m.BranchId; s.Semester = m.Semester; s.Credits = m.Credits; s.IsActive = m.IsActive;
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "Subject code already exists."); return View("Form", m); }
        await _audit.LogAsync(m.Id.HasValue ? "SubjectUpdated" : "SubjectCreated", "Subject", s.SubjectCode, branchId: s.BranchId);
        TempData["Success"] = "Subject saved.";
        return RedirectToAction(nameof(Index), new { branchId = s.BranchId, semester = s.Semester });
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Toggle(int id)
    {
        var s = await _db.Subjects.FindAsync(id);
        if (s == null) return NotFound();
        s.IsActive = !s.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(s.IsActive ? "SubjectActivated" : "SubjectDeactivated", "Subject", s.SubjectCode, branchId: s.BranchId);
        return RedirectToAction(nameof(Index), new { branchId = s.BranchId, semester = s.Semester });
    }

    private async Task<List<SelectListItem>> Branches(int? scope)
    {
        var q = _db.Branches.AsNoTracking().Where(b => b.IsActive);
        if (scope != null) q = q.Where(b => b.Id == scope);
        return await q.OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name + " (" + b.Code + ")", b.Id.ToString())).ToListAsync();
    }
}
