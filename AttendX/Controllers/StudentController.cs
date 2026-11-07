using System.Text.RegularExpressions;

namespace AttendX.Controllers;

/// <summary>The student dashboard. Read-only; every query is filtered by the logged-in student's identity (no id in the URL).</summary>
[Authorize(Policy = PolicyNames.StudentOnly)]
public class StudentController(IStudentPortalService portal, IExportService export, IQrService qr, AppDbContext db, ICurrentUser user, IAuditService audit) : Controller
{
    private readonly IStudentPortalService _portal = portal;
    private readonly IExportService _export = export;
    private readonly IQrService _qr = qr;
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly IAuditService _audit = audit;

    public async Task<IActionResult> Index()
    {
        var vm = await _portal.BuildAsync();
        return vm == null ? View("NoStudent") : View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Query(DateTime date, int subjectId, string message)
    {
        var (ok, msg) = await _portal.RaiseQueryAsync(date, subjectId, message);
        TempData[ok ? "Success" : "Error"] = msg;
        return Redirect(Url.Action(nameof(Index)) + "#queries");
    }

    public async Task<IActionResult> Report()
    {
        var t = await _portal.BuildReportAsync();
        if (t == null) return NotFound();
        return File(_export.ToPdf(t), "application/pdf", $"My_Attendance_{DateTime.Now:yyyyMMdd}.pdf");
    }

    private async Task<Student?> Me()
    {
        var uid = _user.UserId;
        return uid == null ? null : await _db.Students.FirstOrDefaultAsync(s => s.IdentityUserId == uid && s.IsActive);
    }

    public async Task<IActionResult> MyQr(bool download = false)
    {
        var s = await Me();
        if (s == null) return NotFound();
        var bytes = _qr.RenderPng(_qr.GetStudentPayload(s));
        return download ? File(bytes, "image/png", $"QR_{s.StudentID}.png") : File(bytes, "image/png");
    }

    public async Task<IActionResult> Card()
    {
        var s = await _db.Students.AsNoTracking().Include(x => x.Branch).Include(x => x.Section).FirstOrDefaultAsync(x => x.IdentityUserId == _user.UserId && x.IsActive);
        return s == null ? NotFound() : View(s);
    }

    [HttpPost]
    public async Task<IActionResult> UpdatePhone(string phone)
    {
        var s = await Me();
        if (s == null) return NotFound();
        phone = (phone ?? "").Trim();
        if (!Regex.IsMatch(phone, @"^[0-9+\- ]{10,15}$")) { TempData["Error"] = "Enter a valid phone number (10-15 digits)."; return RedirectToAction(nameof(Index)); }
        var old = s.Phone;
        s.Phone = phone; s.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("StudentPhoneChanged", "Student", s.StudentID, old, phone, branchId: s.BranchId);
        TempData["Success"] = "Phone number updated.";
        return RedirectToAction(nameof(Index));
    }
}
