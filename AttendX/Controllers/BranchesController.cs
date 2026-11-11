using System.Text.Json;

namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AdminOnly)]
public class BranchesController(AppDbContext db, IAuditService audit) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly IAuditService _audit = audit;

    public async Task<IActionResult> Index()
    {
        ViewBag.SectionCounts = await _db.Sections.GroupBy(s => s.BranchId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        return View(await _db.Branches.AsNoTracking().OrderBy(b => b.Name).ToListAsync());
    }

    public IActionResult Create() => View("Form", new BranchFormViewModel());

    public async Task<IActionResult> Edit(int id)
    {
        var b = await _db.Branches.FindAsync(id);
        return b == null ? NotFound() : View("Form", new BranchFormViewModel { Id = b.Id, Name = b.Name, Code = b.Code, IsActive = b.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> Save(BranchFormViewModel m)
    {
        if (!ModelState.IsValid) return View("Form", m);
        var code = (m.Code ?? "").Trim().ToUpperInvariant(); var name = (m.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code)) { ModelState.AddModelError("", "Name and code are required."); return View("Form", m); }
        if (await _db.Branches.AnyAsync(b => b.Id != (m.Id ?? 0) && (b.Code.ToUpper() == code || b.Name.ToUpperInvariant() == name.ToUpperInvariant())))
        { ModelState.AddModelError("", "A branch with this name or code already exists."); return View("Form", m); }
        try
        {
            Branch b; string? old = null;
            if (m.Id.HasValue)
            {
                b = await _db.Branches.FindAsync(m.Id.Value) ?? throw new InvalidOperationException("Branch not found.");
                old = JsonSerializer.Serialize(new { b.Name, b.Code, b.IsActive });
            }
            else { b = new Branch(); _db.Branches.Add(b); }
            b.Name = name; b.Code = code; b.IsActive = m.IsActive;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(m.Id.HasValue ? "BranchUpdated" : "BranchCreated", "Branch", b.Code, old, JsonSerializer.Serialize(new { b.Name, b.Code, b.IsActive }), branchId: b.Id);
            TempData["Success"] = "Branch saved.";
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("", "A branch with this name or code already exists.");
            return View("Form", m);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Toggle(int id)
    {
        var b = await _db.Branches.FindAsync(id);
        if (b == null) return NotFound();
        b.IsActive = !b.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(b.IsActive ? "BranchActivated" : "BranchDeactivated", "Branch", b.Code, branchId: b.Id);
        return RedirectToAction(nameof(Index));
    }
}
