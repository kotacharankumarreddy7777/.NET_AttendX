using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

public class MarkPostModel
{
    public int TimetableId { get; set; }
    public DateTime Date { get; set; }
    public Dictionary<int, string> Status { get; set; } = new();
    public string? Reason { get; set; }
    public string? DeviceId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

public class ScanRequest
{
    public int TimetableId { get; set; }
    public string Payload { get; set; } = "";
    public string Token { get; set; } = "";
    public string? DeviceId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

[Authorize(Policy = PolicyNames.AnyStaff)]
public class AttendanceController(IAttendanceService att, ICurrentUser user, AppDbContext db, IClock clock, ICalendarService calendar) : Controller
{
    private readonly IAttendanceService _att = att;
    private readonly ICurrentUser _user = user;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ICalendarService _calendar = calendar;

    /// <summary>Today's classes: faculty see their own, HOD their department, admin everything.</summary>
    public async Task<IActionResult> Index()
    {
        var today = _clock.Now.Date;
        int? facultyId = null, branchId = null;
        if (!_user.IsAdmin)
        {
            if (_user.IsHod) branchId = await _user.GetScopeBranchIdAsync();
            else facultyId = (await _user.GetFacultyAsync())?.Id ?? -1;
        }
        ViewBag.Today = today;
        ViewBag.Mine = facultyId != null;
        ViewBag.NonWorking = await _calendar.GetNonWorkingReasonAsync(today);
        return View(await _att.GetSlotsAsync(today, facultyId, branchId, null));
    }

    [HttpGet]
    public async Task<IActionResult> Mark(int id, DateTime? date)
    {
        var (page, error) = await _att.BuildMarkPageAsync(id, date ?? _clock.Now.Date);
        if (page == null) { TempData["Error"] = error; return RedirectToAction(nameof(Index)); }
        return View(page);
    }

    [HttpPost]
    public async Task<IActionResult> Mark(MarkPostModel m)
    {
        var result = await _att.SaveManualAsync(m.TimetableId, m.Date, m.Status, m.Reason, new DeviceInfo(Clean(m.DeviceId), m.Latitude, m.Longitude));
        TempData[result.Ok ? "Success" : "Error"] = result.Message;
        return result.Ok ? RedirectToAction(nameof(Mark), new { id = m.TimetableId, date = m.Date.ToString("yyyy-MM-dd") }) : RedirectToAction(nameof(Mark), new { id = m.TimetableId, date = m.Date.ToString("yyyy-MM-dd") });
    }

    [HttpGet]
    public async Task<IActionResult> Scan(int id)
    {
        var (page, error) = await _att.BuildMarkPageAsync(id, _clock.Now.Date);
        if (page == null || !page.CanScanQr) { TempData["Error"] = error ?? "QR scanning is available only to the assigned faculty during the period."; return RedirectToAction(nameof(Index)); }
        return View(page);
    }

    [HttpGet]
    public async Task<IActionResult> SessionToken(int id)
    {
        var (token, expires, error) = await _att.IssueSessionTokenAsync(id);
        if (token == null) return StatusCode(403, new { error });
        return Json(new { token, expiresIn = expires });
    }

    [HttpPost]
    public async Task<IActionResult> ScanPost([FromBody] ScanRequest r)
    {
        var res = await _att.ScanAsync(r.TimetableId, r.Payload ?? "", r.Token ?? "", new DeviceInfo(Clean(r.DeviceId), r.Latitude, r.Longitude));
        return Json(new { code = res.Code.ToString(), message = res.Message, studentName = res.StudentName, studentCode = res.StudentCode, status = res.Status });
    }

    [HttpPost]
    public async Task<IActionResult> Sync([FromBody] List<OfflineRecord> records)
        => Json(await _att.SyncOfflineAsync(records ?? new List<OfflineRecord>()));

    /// <summary>Admin / HOD: pick a past class and correct its attendance (reason required, fully audited).</summary>
    [Authorize(Policy = PolicyNames.AdminOrHod)]
    public async Task<IActionResult> Corrections(DateTime? date, int? branchId, int? sectionId)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        if (scope != null) branchId = scope;
        var d = (date ?? _clock.Now.Date).Date;
        if (d > _clock.Now.Date) d = _clock.Now.Date;
        var vm = new CorrectionPickerViewModel { Date = d, BranchId = branchId, SectionId = sectionId };
        vm.Branches = await _db.Branches.AsNoTracking().Where(b => b.IsActive && (scope == null || b.Id == scope)).OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToListAsync();
        if (branchId != null) vm.Sections = await _db.Sections.AsNoTracking().Where(s => s.BranchId == branchId && s.IsActive).OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
        if (sectionId != null) vm.Slots = await _att.GetSlotsAsync(d, null, branchId, sectionId);
        return View(vm);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : (s.Length > 64 ? s[..64] : s);
}
