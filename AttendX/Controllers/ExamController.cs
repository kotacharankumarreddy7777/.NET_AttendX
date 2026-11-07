namespace AttendX.Controllers;

public class ExamController(AppDbContext db, ICurrentUser user) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;

    [Authorize(Policy = PolicyNames.StudentOnly)]
    public async Task<IActionResult> Index(string examType, int? semester)
    {
        var uid = _user.UserId;
        var me = uid == null ? null : await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.IdentityUserId == uid && s.IsActive);
        if (me == null) return NotFound();

        int sem = semester ?? me.Semester;
        var q = _db.ExamTimetables.AsNoTracking()
            .Include(x => x.Subject).Include(x => x.Faculty).Include(x => x.AcademicYear)
            .Where(x => x.IsActive && x.BranchId == me.BranchId && x.Semester == sem);

        if (!string.IsNullOrEmpty(examType)) q = q.Where(x => x.ExamType == examType);

        var exams = await q.OrderBy(x => x.ExamDate).ThenBy(x => x.StartTime).ToListAsync();
        ViewBag.Semester = sem;
        ViewBag.ExamType = examType;
        ViewBag.IsStudent = true;
        return View(exams);
    }

    [Authorize(Policy = PolicyNames.AdminOrHod)]
    public async Task<IActionResult> Admin(int? semester, int? branchId, string examType, int? yearId)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var q = _db.ExamTimetables.AsNoTracking()
            .Include(x => x.Subject).Include(x => x.Faculty).Include(x => x.AcademicYear).Include(x => x.Branch)
            .Where(x => x.IsActive);

        if (scope != null) q = q.Where(x => x.BranchId == scope);
        if (branchId != null && _user.IsAdmin) q = q.Where(x => x.BranchId == branchId);
        if (semester != null) q = q.Where(x => x.Semester == semester);
        if (yearId != null) q = q.Where(x => x.AcademicYearId == yearId);
        if (!string.IsNullOrEmpty(examType)) q = q.Where(x => x.ExamType == examType);

        var exams = await q.OrderBy(x => x.ExamDate).ToListAsync();
        ViewBag.Semester = semester;
        ViewBag.BranchId = branchId;
        ViewBag.ExamType = examType;
        ViewBag.YearId = yearId;
        ViewBag.Branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).Select(b => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(b.Name, b.Id.ToString())).ToListAsync();
        return View(exams);
    }
}
