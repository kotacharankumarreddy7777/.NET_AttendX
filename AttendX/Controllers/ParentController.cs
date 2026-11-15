namespace AttendX.Controllers;

/// <summary>Parent portal - view child's attendance, leaves, and apply for leave on their behalf.</summary>
[Authorize(Policy = PolicyNames.ParentOnly)]
public class ParentController(AppDbContext db, ICurrentUser user, ILeaveService leave, INotificationService notification) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly ILeaveService _leave = leave;
    private readonly INotificationService _notification = notification;

    public async Task<IActionResult> Index()
    {
        var parent = await _db.Parents.AsNoTracking().FirstOrDefaultAsync(p => p.IdentityUserId == _user.UserId && p.IsActive);
        if (parent == null) return View("NoParent");
        var student = await _db.Students.AsNoTracking().Include(s => s.Branch).Include(s => s.Section).FirstOrDefaultAsync(s => s.Id == parent.StudentId);
        if (student == null) return View("NoStudent");

        var settings = await _db.AttendanceSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        var rows = await _db.Attendance.AsNoTracking().Where(a => a.StudentId == student.Id).ToListAsync();
        var percent = rows.Count == 0 ? 100m : AttendX.Services.AttendanceMath.Percent(
            rows.Count(a => a.Status == AttendX.Models.AttendanceStatus.Present),
            rows.Count(a => a.Status == AttendX.Models.AttendanceStatus.Late),
            rows.Count, settings?.LateWeight ?? 1m);

        var leaves = await _leave.GetLeaveApplicationsAsync(student.Id, null);
        var leaveTypes = await _leave.GetLeaveTypesAsync();

        // Recent exam marks for the ward
        var marks = await _db.ExamMarks.AsNoTracking()
            .Include(m => m.ExamTimetable)!.ThenInclude(tt => tt!.Exam)
            .Include(m => m.ExamTimetable)!.ThenInclude(tt => tt!.Subject)
            .Where(m => m.StudentId == student.Id)
            .OrderByDescending(m => m.MarkedAt)
            .Take(20)
            .ToListAsync();

        // Ward's upcoming exam timetable
        var today = DateTime.UtcNow.Date;
        var wardExams = await _db.ExamTimetables.AsNoTracking()
            .Include(x => x.Exam).Include(x => x.Subject)
            .Where(x => x.IsActive && x.BranchId == student.BranchId && x.SectionId == student.SectionId && x.Semester == student.Semester)
            .Where(x => x.ExamDate.Date >= today)
            .OrderBy(x => x.ExamDate).ThenBy(x => x.StartTime)
            .Take(30)
            .ToListAsync();
        var regs = await _db.ExamRegistrations.AsNoTracking()
            .Where(r => r.StudentId == student.Id)
            .ToDictionaryAsync(r => r.ExamId, r => r.IsEligible);
        var eligible = regs.Values.All(v => v);

        ViewBag.Parent = parent;
        ViewBag.Student = student;
        ViewBag.Percent = percent;
        ViewBag.Leaves = leaves;
        ViewBag.LeaveTypes = leaveTypes;
        ViewBag.Marks = marks;
        ViewBag.WardExams = wardExams;
        ViewBag.WardExamEligibility = regs;
        ViewBag.HallTicketEligible = eligible && percent >= 75m;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> ApplyLeave(int leaveTypeId, DateTime startDate, DateTime endDate, string reason)
    {
        var parent = await _db.Parents.FirstOrDefaultAsync(p => p.IdentityUserId == _user.UserId && p.IsActive);
        if (parent == null) return Unauthorized();

        var (ok, err) = await _leave.ApplyLeaveAsync(parent.StudentId, leaveTypeId, startDate, endDate, reason);
        TempData[ok ? "Success" : "Error"] = err ?? "Leave application submitted on behalf of your ward.";
        return RedirectToAction(nameof(Index));
    }
}
