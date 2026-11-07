using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AdminOrHod)]
public class FacultyController(AppDbContext db, IFacultyService faculty, ICurrentUser user) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly IFacultyService _faculty = faculty;
    private readonly ICurrentUser _user = user;

    private async Task<List<SelectListItem>> Branches()
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var q = _db.Branches.AsNoTracking().Where(b => b.IsActive);
        if (scope != null) q = q.Where(b => b.Id == scope);
        return await q.OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name + " (" + b.Code + ")", b.Id.ToString())).ToListAsync();
    }

    public async Task<IActionResult> Index(int? branchId, string? search, string status = "active")
    {
        var scope = await _user.GetScopeBranchIdAsync();
        if (scope != null) branchId = scope;
        var q = _db.Faculty.AsNoTracking().Include(f => f.Branch).AsQueryable();
        if (status == "active") q = q.Where(f => f.IsActive); else if (status == "inactive") q = q.Where(f => !f.IsActive);
        if (branchId != null) q = q.Where(f => f.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(f => f.FullName.Contains(search) || f.Email.Contains(search) || f.FacultyID.Contains(search));
        ViewBag.Branches = await Branches(); ViewBag.BranchId = branchId; ViewBag.Search = search; ViewBag.Status = status; ViewBag.Locked = scope != null;
        return View(await q.OrderBy(f => f.FacultyID).Take(500).ToListAsync());
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Create() => View("Form", new FacultyFormViewModel { Branches = await Branches() });

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Create(FacultyFormViewModel m)
    {
        if (ModelState.IsValid)
        {
            var (f, pwd, err) = await _faculty.CreateAsync(m);
            if (f != null)
            {
                TempData["TempPassword"] = $"Login created for {f.Email}. Temporary password (shown ONCE, user must change it): {pwd}";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError("", err ?? "Could not create faculty.");
        }
        m.Branches = await Branches();
        return View("Form", m);
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Edit(int id)
    {
        var f = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (f == null) return NotFound();
        return View("Form", new FacultyFormViewModel { Id = f.Id, FacultyID = f.FacultyID, FullName = f.FullName, Email = f.Email, Phone = f.Phone, BranchId = f.BranchId ?? 0, Designation = f.Designation, IsHOD = f.IsHOD, Branches = await Branches() });
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Edit(int id, FacultyFormViewModel m)
    {
        if (ModelState.IsValid)
        {
            var (ok, err) = await _faculty.UpdateAsync(id, m);
            if (ok) { TempData["Success"] = "Faculty updated."; return RedirectToAction(nameof(Index)); }
            ModelState.AddModelError("", err ?? "Could not update faculty.");
        }
        m.Id = id; m.Branches = await Branches();
        return View("Form", m);
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> SetActive(int id, bool active)
    {
        var (ok, err) = await _faculty.SetActiveAsync(id, active);
        TempData[ok ? "Success" : "Error"] = ok ? (active ? "Faculty activated." : "Faculty deactivated - the login is blocked.") : err;
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public IActionResult Import() => View();

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Import(IFormFile? file, string? initialPassword)
    {
        if (file == null || file.Length == 0) { TempData["Error"] = "Choose a CSV file."; return View(); }
        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || file.Length > 2 * 1024 * 1024)
        { TempData["Error"] = "Only .csv files up to 2 MB are accepted."; return View(); }
        if (string.IsNullOrWhiteSpace(initialPassword)) { TempData["Error"] = "Enter an initial password for the imported accounts."; return View(); }
        using var sr = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        // all imported accounts get this initial password and MUST change it at first login
        var result = await _faculty.ImportCsvAsync(await sr.ReadToEndAsync(), initialPassword.Trim(), mustChange: true);
        return View(result);
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public IActionResult Template()
    {
        var csv = "FacultyID,FullName,Email,Phone,Department,Designation,IsHOD\r\n,Dr. Example Name,example.name@college.edu,9876543210,CSE,Assistant Professor,false\r\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "faculty_template.csv");
    }
}
