namespace AttendX.Controllers;

/// <summary>Leave management for students and admin approval.</summary>
[Authorize]
public class LeaveController(AppDbContext db, ILeaveService leave, ICurrentUser user) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ILeaveService _leave = leave;
    private readonly ICurrentUser _user = user;

    // ---------- Student: Apply for leave ----------
    public async Task<IActionResult> Index()
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var isStudent = User.IsInRole(RoleNames.Student);
        var isAdminOrHod = User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.Hod);

        int? studentId = null;
        if (isStudent)
        {
            var st = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.IdentityUserId == _user.UserId);
            studentId = st?.Id;
        }

        var apps = await _leave.GetLeaveApplicationsAsync(studentId, null);
        var types = await _leave.GetLeaveTypesAsync();
        ViewBag.Types = types;
        ViewBag.IsStudent = isStudent;
        ViewBag.IsAdminOrHod = isAdminOrHod;
        ViewBag.Scope = scope;
        return View(apps);
    }

    [HttpPost]
    public async Task<IActionResult> Apply(int leaveTypeId, DateTime startDate, DateTime endDate, string reason)
    {
        int? studentId = null;
        if (User.IsInRole(RoleNames.Student))
        {
            var st = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.IdentityUserId == _user.UserId);
            studentId = st?.Id;
        }
        if (studentId == null) return Unauthorized();

        var (ok, err) = await _leave.ApplyLeaveAsync(studentId.Value, leaveTypeId, startDate, endDate, reason);
        TempData[ok ? "Success" : "Error"] = err ?? "Leave application submitted.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Admin/HOD: Review leave ----------
    [Authorize(Policy = PolicyNames.AdminOrHod)]
    public async Task<IActionResult> Review(int id, string status, string? adminResponse)
    {
        var (ok, err) = await _leave.ReviewLeaveAsync(id, status, adminResponse);
        TempData[ok ? "Success" : "Error"] = err ?? "Leave application reviewed.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Leave balance ----------
    public async Task<IActionResult> Balance(int? studentId)
    {
        if (studentId == null && User.IsInRole(RoleNames.Student))
        {
            var st = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.IdentityUserId == _user.UserId);
            studentId = st?.Id;
        }
        if (studentId == null) return NotFound();

        var balance = await _leave.GetLeaveBalanceAsync(studentId.Value);
        var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId);
        ViewBag.Student = student;
        return View(balance);
    }
}
