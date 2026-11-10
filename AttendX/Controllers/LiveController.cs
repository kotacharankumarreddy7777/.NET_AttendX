namespace AttendX.Controllers;

/// <summary>Lightweight JSON endpoints for live-sync polling (no page reload).</summary>
[Authorize(Policy = PolicyNames.AnyStaff)]
public class LiveController(AppDbContext db, INoticeService notices, ICurrentUser user, IDashboardService dash, IClock clock) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly INoticeService _notices = notices;
    private readonly ICurrentUser _user = user;
    private readonly IDashboardService _dash = dash;
    private readonly IClock _clock = clock;

    [HttpGet]
    public async Task<IActionResult> Bell()
    {
        if (!(_user.IsAdmin || _user.IsHod || _user.IsFaculty)) return Forbid();
        var count = await _notices.RecentCountAsync();
        var latest = await _notices.VisibleAsync(3);
        return Json(new
        {
            count,
            latest = latest.Select(n => new { title = n.Title, date = n.PublishedAt.ToString("hh:mm tt, dd MMM") }).ToArray()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Health()
    {
        if (!(_user.IsAdmin || _user.IsHod || _user.IsFaculty)) return Forbid();
        var today = _clock.Now.Date;
        var scope = await _user.GetScopeBranchIdAsync();
        bool isGlobal = _user.IsAdmin;
        var students = isGlobal
            ? await _db.Students.CountAsync(s => s.IsActive)
            : scope != null ? await _db.Students.CountAsync(s => s.IsActive && s.BranchId == scope) : 0;
        var attendanceToday = isGlobal
            ? await _db.Attendance.CountAsync(a => a.Date == today)
            : scope != null ? await _db.Attendance.CountAsync(a => a.Date == today && a.BranchId == scope) : 0;
        return Json(new { ok = true, students, attendanceToday, serverTime = _clock.Now.ToString("hh:mm tt, dd MMM yyyy") });
    }

    [HttpGet]
    public async Task<IActionResult> Slots(int? yearId, int? branchId, int? sectionId, int? semester)
    {
        if (!(_user.IsAdmin || _user.IsHod || _user.IsFaculty)) return Forbid();
        var today = _clock.Now.Date;
        var now = _clock.Now;
        var q = _db.Timetables.AsNoTracking().Where(t => t.IsActive && t.Day == today.DayOfWeek);
        if (yearId != null) q = q.Where(t => t.AcademicYearId == yearId);
        if (branchId != null) q = q.Where(t => t.BranchId == branchId);
        if (sectionId != null) q = q.Where(t => t.SectionId == sectionId);
        if (semester != null) q = q.Where(t => t.Semester == semester);
        if (!_user.IsAdmin)
        {
            var scope = await _user.GetScopeBranchIdAsync();
            if (scope.HasValue && scope.Value != -1)
            {
                q = q.Where(t => t.BranchId == scope.Value);
            }
            else
            {
                var fid = (await _user.GetFacultyAsync())?.Id ?? -1;
                if (fid == -1) return Forbid();
                q = q.Where(t => t.FacultyId == fid);
            }
        }
        var slots = await q.Select(t => new { t.Period, t.StartTime, t.EndTime }).OrderBy(s => s.Period).ToListAsync();
        var result = slots.Select(s =>
        {
            var start = today + s.StartTime;
            var end = today + s.EndTime;
            string state = now < start ? "Upcoming" : now > end ? "Closed" : "Open";
            return new { period = s.Period, state, start = (today + s.StartTime).ToString("hh:mm tt"), end = (today + s.EndTime).ToString("hh:mm tt") };
        }).ToList();
        return Json(new { slots = result, today = today.ToString("dddd, dd MMM yyyy") });
    }
}
