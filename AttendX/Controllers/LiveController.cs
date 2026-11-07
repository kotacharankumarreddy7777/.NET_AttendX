namespace AttendX.Controllers;

/// <summary>Lightweight JSON endpoints for live-sync polling (no page reload).</summary>
[Authorize]
public class LiveController(AppDbContext db, INoticeService notices, ICurrentUser user, IDashboardService dash) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly INoticeService _notices = notices;
    private readonly ICurrentUser _user = user;
    private readonly IDashboardService _dash = dash;

    [HttpGet]
    public async Task<IActionResult> Bell()
    {
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
        // Lightweight tick for superfast ping - no heavy aggregates
        int students = await _db.Students.CountAsync(s => s.IsActive);
        int attendanceToday = 0;
        try
        {
            var today = DateTime.UtcNow.AddHours(5.5).Date; // IST today
            attendanceToday = await _db.Attendance.CountAsync(a => a.Date == today);
        }
        catch { }
        return Json(new { ok = true, students, attendanceToday, serverTime = DateTime.UtcNow.AddHours(5.5).ToString("hh:mm tt, dd MMM yyyy") });
    }

    [HttpGet]
    public async Task<IActionResult> Slots()
    {
        // Reuse AttendanceService logic via minimal query - read-only slot states for today
        var today = DateTime.UtcNow.AddHours(5.5).Date;
        var now = DateTime.UtcNow.AddHours(5.5);
        var slots = await _db.Timetables.AsNoTracking()
            .Where(t => t.IsActive && t.Day == today.DayOfWeek)
            .Select(t => new { t.Period, t.StartTime, t.EndTime })
            .OrderBy(s => s.Period)
            .ToListAsync();

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
