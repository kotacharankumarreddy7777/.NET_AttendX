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

    /// <summary>Full exam timetable + results for the logged-in student.</summary>
    public async Task<IActionResult> Exams()
    {
        var s = await Me();
        if (s == null) return NotFound();
        var today = DateTime.UtcNow.Date;
        var rows = await _db.ExamTimetables.AsNoTracking()
            .Include(x => x.Exam).Include(x => x.Subject)
            .Where(x => x.IsActive && x.BranchId == s.BranchId && x.SectionId == s.SectionId && x.Semester == s.Semester)
            .OrderBy(x => x.ExamDate).ThenBy(x => x.StartTime)
            .ToListAsync();
        var regs = await _db.ExamRegistrations.AsNoTracking()
            .Where(r => r.StudentId == s.Id)
            .ToDictionaryAsync(r => r.ExamId, r => r.IsEligible);
        var marks = await _db.ExamMarks.AsNoTracking()
            .Include(m => m.ExamTimetable)!.ThenInclude(et => et!.Exam)
            .Include(m => m.ExamTimetable)!.ThenInclude(et => et!.Subject)
            .Where(m => m.StudentId == s.Id)
            .OrderByDescending(m => m.MarkedAt)
            .ToListAsync();
        ViewBag.Student = s;
        ViewBag.Today = today;
        ViewBag.Registrations = regs;
        ViewBag.Marks = marks;
        return View(rows);
    }

    /// <summary>Printable hall ticket gated by attendance eligibility.</summary>
    public async Task<IActionResult> HallTicket()
    {
        var s = await Me();
        if (s == null) return NotFound();
        var settings = await _db.AttendanceSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        var rows = await _db.Attendance.AsNoTracking().Where(a => a.StudentId == s.Id).ToListAsync();
        var percent = rows.Count == 0 ? 100m : AttendX.Services.AttendanceMath.Percent(
            rows.Count(a => a.Status == AttendX.Models.AttendanceStatus.Present),
            rows.Count(a => a.Status == AttendX.Models.AttendanceStatus.Late),
            rows.Count, settings?.LateWeight ?? 1m);
        var threshold = settings?.DefaultThreshold ?? 75m;
        var eligible = percent >= threshold;
        var exams = await _db.ExamTimetables.AsNoTracking()
            .Include(x => x.Exam).Include(x => x.Subject)
            .Where(x => x.IsActive && x.BranchId == s.BranchId && x.SectionId == s.SectionId && x.Semester == s.Semester)
            .Where(x => x.ExamDate.Date >= DateTime.UtcNow.Date)
            .OrderBy(x => x.ExamDate).ThenBy(x => x.StartTime)
            .ToListAsync();
        ViewBag.Student = s;
        ViewBag.Percent = percent;
        ViewBag.Threshold = threshold;
        ViewBag.Eligible = eligible;
        ViewBag.Exams = exams;
        return View();
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
        return File(_export.ToPdf(t), "application/pdf", $"My_Attendance_{DateTime.UtcNow:yyyyMMdd}.pdf");
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
        s.Phone = phone; s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("StudentPhoneChanged", "Student", s.StudentID, old, phone, branchId: s.BranchId);
        TempData["Success"] = "Phone number updated.";
        return RedirectToAction(nameof(Index));
    }
}

