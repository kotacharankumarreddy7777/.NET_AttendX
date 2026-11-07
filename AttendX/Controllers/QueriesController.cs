namespace AttendX.Controllers;

/// <summary>Inbox of student attendance queries for Faculty (own classes), HOD (department) and Admin (all).</summary>
[Authorize(Policy = PolicyNames.AnyStaff)]
public class QueriesController(AppDbContext db, ICurrentUser user, IAuditService audit) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly IAuditService _audit = audit;

    private async Task<IQueryable<AttendanceQuery>> Scoped()
    {
        IQueryable<AttendanceQuery> q = _db.AttendanceQueries;
        if (_user.IsAdmin) return q;
        if (_user.IsHod) { var b = await _user.GetScopeBranchIdAsync(); return q.Where(x => x.BranchId == b); }
        var me = await _user.GetFacultyAsync();
        var fid = me?.Id ?? -1;
        return q.Where(x => x.FacultyId == fid);
    }

    public async Task<IActionResult> Index(string status = "Open")
    {
        var q = (await Scoped()).AsNoTracking().Include(x => x.Student).Include(x => x.Subject).AsQueryable();
        if (status != "All") q = q.Where(x => x.Status == status);
        ViewBag.Status = status;
        return View(await q.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Respond(int id, string response, string decision)
    {
        var q = await (await Scoped()).Include(x => x.Student).FirstOrDefaultAsync(x => x.Id == id);
        if (q == null) return NotFound();
        response = (response ?? "").Trim();
        if (response.Length < 3 || response.Length > 500) { TempData["Error"] = "Write a short answer (3-500 characters)."; return RedirectToAction(nameof(Index)); }
        q.Status = decision == "reject" ? "Rejected" : "Resolved";
        q.Response = response; q.ResponderName = _user.DisplayName; q.RespondedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("QueryAnswered", "AttendanceQuery", q.Student!.StudentID, newValues: q.Status, reason: response, branchId: q.BranchId);
        TempData["Success"] = "Reply sent to the student.";
        return RedirectToAction(nameof(Index));
    }
}
