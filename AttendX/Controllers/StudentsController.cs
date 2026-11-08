using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AdminOrHod)]
public class StudentsController(AppDbContext db, IStudentService students, IQrService qr, ICurrentUser user, ISettingsService settings) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly IStudentService _students = students;
    private readonly IQrService _qr = qr;
    private readonly ICurrentUser _user = user;
    private readonly ISettingsService _settings = settings;

    // ---------- helpers
    private async Task<List<SelectListItem>> BranchItems()
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var q = _db.Branches.AsNoTracking().Where(b => b.IsActive);
        if (scope != null) q = q.Where(b => b.Id == scope);
        return await q.OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name + " (" + b.Code + ")", b.Id.ToString())).ToListAsync();
    }
    private async Task<List<SelectListItem>> SectionItems(int? branchId)
    {
        if (branchId == null) return new();
        return await _db.Sections.AsNoTracking().Where(s => s.BranchId == branchId && s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
    }
    private async Task<bool> InScope(int branchId)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        return scope == null || scope == branchId;
    }
    private async Task Fill(StudentFormViewModel m)
    {
        m.Branches = await BranchItems();
        m.Sections = await SectionItems(m.BranchId == 0 ? null : m.BranchId);
    }

    // ---------- list
    public async Task<IActionResult> Index(string? search, int? branchId, int? sectionId, int? semester, int? year, string status = "active", int page = 1)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        if (scope != null) branchId = scope;
        var q = _db.Students.AsNoTracking().Include(s => s.Branch).Include(s => s.Section).AsQueryable();
        if (status == "active") q = q.Where(s => s.IsActive); else if (status == "inactive") q = q.Where(s => !s.IsActive);
        if (branchId != null) q = q.Where(s => s.BranchId == branchId);
        if (sectionId != null) q = q.Where(s => s.SectionId == sectionId);
        if (semester != null) q = q.Where(s => s.Semester == semester);
        if (year != null) q = q.Where(s => s.Year == year);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim();
            q = q.Where(s => s.FullName.Contains(t) || s.StudentID.Contains(t) || s.Email.Contains(t) || s.Phone.Contains(t));
        }
        page = Math.Max(1, page);
        var vm = new StudentListViewModel { Search = search, BranchId = branchId, SectionId = sectionId, Semester = semester, Year = year, Status = status };
        vm.Page.Page = page; vm.Page.PageSize = 25; vm.Page.TotalCount = await q.CountAsync();
        vm.Page.Items = await q.OrderBy(s => s.StudentID).Skip((page - 1) * 25).Take(25).ToListAsync();
        vm.Branches = await BranchItems(); vm.Sections = await SectionItems(branchId);
        ViewBag.Locked = scope != null;
        return View(vm);
    }

    // ---------- details
    public async Task<IActionResult> Details(int id)
    {
        var s = await _db.Students.AsNoTracking().Include(x => x.Branch).Include(x => x.Section).FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        if (!await InScope(s.BranchId)) return Forbid();
        var settings = await _settings.GetAsync();
        var rules = await _db.ThresholdRules.AsNoTracking().ToListAsync();
        var g = await _db.Attendance.AsNoTracking().Where(a => a.StudentId == id).GroupBy(a => a.SubjectId).Select(x => new
        {
            x.Key, T = x.Count(), P = x.Count(a => a.Status == AttendanceStatus.Present), L = x.Count(a => a.Status == AttendanceStatus.Late), A = x.Count(a => a.Status == AttendanceStatus.Absent)
        }).ToListAsync();
        var sids = g.Select(x => x.Key).ToList();
        var subs = await _db.Subjects.AsNoTracking().Where(x => sids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var vm = new StudentDetailsViewModel
        {
            Student = s,
            Total = g.Sum(x => x.T),
            Threshold = ThresholdResolver.Resolve(rules, s.BranchId, s.Semester, s.SectionId, settings.DefaultThreshold)
        };
        vm.OverallPercent = AttendanceMath.Percent(g.Sum(x => x.P), g.Sum(x => x.L), vm.Total, settings.LateWeight);
        vm.Risk = AttendanceMath.Risk(vm.OverallPercent, vm.Threshold);
        vm.Subjects = [.. g.Select(x => new SubjectPercent
        {
            Subject = subs[x.Key].SubjectCode + " " + subs[x.Key].SubjectName, Total = x.T, Present = x.P, Late = x.L, Absent = x.A,
            Percent = AttendanceMath.Percent(x.P, x.L, x.T, settings.LateWeight)
        }).OrderBy(x => x.Percent)];
        vm.Recent = await _db.Attendance.AsNoTracking().Include(a => a.Subject).Where(a => a.StudentId == id).OrderByDescending(a => a.Date).ThenByDescending(a => a.Period).Take(40).ToListAsync();
        return View(vm);
    }

    // ---------- create / edit (Admin only)
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Create()
    {
        var m = new StudentFormViewModel(); await Fill(m);
        return View("Form", m);
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Create(StudentFormViewModel m)
    {
        if (ModelState.IsValid)
        {
            var (s, err) = await _students.CreateAsync(m);
            if (s != null) { TempData["Success"] = $"Student created. Student ID: {s.StudentID}."; return RedirectToAction(nameof(Details), new { id = s.Id }); }
            ModelState.AddModelError("", err ?? "Could not create student.");
        }
        await Fill(m);
        return View("Form", m);
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Edit(int id)
    {
        var s = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        var m = new StudentFormViewModel
        {
            Id = s.Id, FullName = s.FullName, Email = s.Email, Phone = s.Phone, BranchId = s.BranchId, SectionId = s.SectionId, Semester = s.Semester,
            JoiningDate = s.JoiningDate, ExistingPhoto = s.PhotoPath, StudentCode = s.StudentID
        };
        await Fill(m);
        return View("Form", m);
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Edit(int id, StudentFormViewModel m)
    {
        if (ModelState.IsValid)
        {
            var (ok, err) = await _students.UpdateAsync(id, m);
            if (ok) { TempData["Success"] = "Student updated."; return RedirectToAction(nameof(Details), new { id }); }
            ModelState.AddModelError("", err ?? "Could not update student.");
        }
        m.Id = id;
        var cur = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        m.ExistingPhoto = cur?.PhotoPath; m.StudentCode = cur?.StudentID;
        await Fill(m);
        return View("Form", m);
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> SetActive(int id, bool active)
    {
        var (ok, err) = await _students.SetActiveAsync(id, active);
        TempData[ok ? "Success" : "Error"] = ok ? (active ? "Student reactivated." : "Student deactivated (history kept).") : err;
        return RedirectToAction(nameof(Index));
    }

    // ---------- QR
    public async Task<IActionResult> Qr(int id, bool download = false)
    {
        var s = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        if (!await InScope(s.BranchId)) return Forbid();
        var bytes = _qr.RenderPng(_qr.GetStudentPayload(s));
        return download ? File(bytes, "image/png", $"QR_{s.StudentID}.png") : File(bytes, "image/png");
    }

    public async Task<IActionResult> Card(int id)
    {
        var s = await _db.Students.AsNoTracking().Include(x => x.Branch).Include(x => x.Section).FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        if (!await InScope(s.BranchId)) return Forbid();
        return View("Cards", new List<Student> { s });
    }

    /// <summary>Printable sheet of ID cards for a whole section.</summary>
    public async Task<IActionResult> Cards(int branchId, int sectionId, int? semester)
    {
        if (!await InScope(branchId)) return Forbid();
        var q = _db.Students.AsNoTracking().Include(x => x.Branch).Include(x => x.Section).Where(x => x.IsActive && x.BranchId == branchId && x.SectionId == sectionId);
        if (semester != null) q = q.Where(x => x.Semester == semester);
        return View(await q.OrderBy(x => x.StudentID).Take(200).ToListAsync());
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> RegenerateQr(int id)
    {
        var n = await _students.GenerateQrAsync(new[] { id }, true);
        TempData["Success"] = n > 0 ? "QR code regenerated." : "Nothing to do.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> BulkQr(int? branchId, int? sectionId, int? semester, bool regenerate)
    {
        if (branchId == null && sectionId == null && semester == null) { TempData["Error"] = "Select a filter (branch, section or semester) before generating QRs."; return RedirectToAction(nameof(Index)); }
        if (!await InScope(branchId ?? 0) && branchId != null) return Forbid();
        var q = _db.Students.AsNoTracking().Where(s => s.IsActive);
        if (branchId != null) q = q.Where(s => s.BranchId == branchId);
        if (sectionId != null) q = q.Where(s => s.SectionId == sectionId);
        if (semester != null) q = q.Where(s => s.Semester == semester);
        var n = await _students.GenerateQrAsync(await q.Select(s => s.Id).ToListAsync(), regenerate);
        TempData["Success"] = $"{n} QR code(s) generated.";
        return RedirectToAction(nameof(Index), new { branchId, sectionId, semester });
    }

    // ---------- CSV import
    [Authorize(Policy = PolicyNames.AdminOnly)]
    public IActionResult Import() => View();

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        if (file == null || file.Length == 0) { TempData["Error"] = "Choose a CSV file."; return View(); }
        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || file.Length > 2 * 1024 * 1024)
        { TempData["Error"] = "Only .csv files up to 2 MB are accepted."; return View(); }
        var allowedTypes = new[] { "text/csv", "application/vnd.ms-excel" };
        if (!string.IsNullOrEmpty(file.ContentType) && !allowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        { TempData["Error"] = "Only CSV files are accepted."; return View(); }
        using var sr = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var result = await _students.ImportCsvAsync(await sr.ReadToEndAsync());
        return View(result);
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public IActionResult Template()
    {
        var csv = "FullName,Email,Phone,BranchCode,Section,Semester,JoiningDate\r\nAarav Patel,aarav.patel@example.com,9876543210,CSE,EV1,5,2024-07-15\r\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "students_template.csv");
    }
}
