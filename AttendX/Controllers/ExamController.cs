using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

/// <summary>Exam management: timetable, registration eligibility, and mark entry.
/// Faculty may view exams + enter marks for their papers; HOD/Admin manage everything.</summary>
[Authorize]
public class ExamController(AppDbContext db, IExamService exam, ICurrentUser user, ISettingsService settings) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly IExamService _exam = exam;
    private readonly ICurrentUser _user = user;
    private readonly ISettingsService _settings = settings;

    // ---------- Exam list ----------
    public async Task<IActionResult> Index()
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var fac = _user.IsAdmin || _user.IsHod ? null : await _user.GetFacultyAsync();
        var exams = await _exam.GetExamsAsync();
        if (fac != null)
        {
            // Faculty see only exams in which they invigilate at least one paper
            var myExamIds = await _db.ExamTimetables.AsNoTracking().Where(t => t.FacultyId == fac.Id).Select(t => t.ExamId).Distinct().ToListAsync();
            exams = exams.Where(e => myExamIds.Contains(e.Id)).ToList();
        }
        ViewBag.Scope = scope;
        return View(exams);
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> Create() => View(new Exam { Semester = DateTime.UtcNow.Month >= 8 ? 1 : 2 });

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> Create(Exam m)
    {
        if (!ModelState.IsValid) return View(m);
        var year = await _settings.GetCurrentYearAsync();
        m.AcademicYearId = year.Id;
        var (ok, err) = await _exam.CreateExamAsync(m);
        if (ok) { TempData["Success"] = "Exam created."; return RedirectToAction(nameof(Index)); }
        ModelState.AddModelError("", err ?? "Could not create exam.");
        return View(m);
    }

    // ---------- Exam timetable ----------
    public async Task<IActionResult> Timetable(int examId)
    {
        var exam = await _exam.GetExamAsync(examId);
        if (exam == null) return NotFound();
        var scope = await _user.GetScopeBranchIdAsync();
        var fac = _user.IsAdmin || _user.IsHod ? null : await _user.GetFacultyAsync();
        var tt = await _exam.GetExamTimetableAsync(examId, scope, null, null);
        if (fac != null) tt = tt.Where(t => t.FacultyId == fac.Id).ToList();   // faculty sees only papers they invigilate
        ViewBag.Exam = exam;
        ViewBag.Scope = scope;
        return View(tt);
    }

    [Authorize(Policy = PolicyNames.AdminOnly)]
    public async Task<IActionResult> AddTimetableSlot(int examId)
    {
        var exam = await _exam.GetExamAsync(examId);
        if (exam == null) return NotFound();
        var scope = await _user.GetScopeBranchIdAsync();
        var vm = new ExamTimetableFormViewModel
        {
            ExamId = examId,
            Semester = exam.Semester,
            Branches = await _db.Branches.AsNoTracking().Where(b => b.IsActive && (scope == null || b.Id == scope)).OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToListAsync(),
            Faculty = await _db.Faculty.AsNoTracking().Where(f => f.IsActive && (scope == null || f.BranchId == scope)).OrderBy(f => f.FullName).Select(f => new SelectListItem(f.FullName, f.Id.ToString())).ToListAsync(),
            Subjects = await _db.Subjects.AsNoTracking().Where(s => s.IsActive && s.Semester == exam.Semester).OrderBy(s => s.SubjectCode).Select(s => new SelectListItem(s.SubjectCode + " - " + s.SubjectName, s.Id.ToString())).ToListAsync()
        };
        return View("TimetableForm", vm);
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> AddTimetableSlot(ExamTimetableFormViewModel m)
    {
        if (!ModelState.IsValid) return View("TimetableForm", m);
        var scope = await _user.GetScopeBranchIdAsync();
        var tt = new ExamTimetable
        {
            ExamId = m.ExamId, SubjectId = m.SubjectId, BranchId = m.BranchId, SectionId = m.SectionId,
            Semester = m.Semester, FacultyId = m.FacultyId, ExamDate = m.ExamDate,
            StartTime = m.StartTime, EndTime = m.EndTime, RoomNo = m.RoomNo.Trim()
        };
        var (ok, err) = await _exam.CreateExamTimetableAsync(tt);
        if (ok) { TempData["Success"] = "Exam slot scheduled."; return RedirectToAction(nameof(Timetable), new { examId = m.ExamId }); }
        ModelState.AddModelError("", err ?? "Could not schedule exam slot.");
        return View("TimetableForm", m);
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> RemoveTimetableSlot(int id, int examId)
    {
        await _exam.DeleteExamTimetableAsync(id);
        TempData["Success"] = "Exam slot removed.";
        return RedirectToAction(nameof(Timetable), new { examId });
    }

    // ---------- Registration / Eligibility ----------
    [Authorize(Policy = PolicyNames.AdminOrHod)]
    public async Task<IActionResult> Registrations(int examId)
    {
        var exam = await _exam.GetExamAsync(examId);
        if (exam == null) return NotFound();
        var regs = await _exam.GetRegistrationsAsync(examId);
        ViewBag.Exam = exam;
        return View(regs);
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> GenerateRegistrations(int examId)
    {
        var (ok, err) = await _exam.GenerateRegistrationsAsync(examId);
        TempData[ok ? "Success" : "Error"] = err ?? "Registrations generated based on attendance eligibility.";
        return RedirectToAction(nameof(Registrations), new { examId });
    }

    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> ToggleEligibility(int id, bool eligible, string? reason, int examId)
    {
        await _exam.ToggleEligibilityAsync(id, eligible, reason);
        return RedirectToAction(nameof(Registrations), new { examId });
    }

    // ---------- Mark entry ----------
    /// <summary>Lists all papers of one exam with links to the marks entry sheet.</summary>
    [Authorize(Policy = PolicyNames.AnyStaff)]
    public async Task<IActionResult> MarksByExam(int examId)
    {
        var exam = await _exam.GetExamAsync(examId);
        if (exam == null) return NotFound();
        var scope = await _user.GetScopeBranchIdAsync();
        var fac = _user.IsAdmin ? null : await _user.GetFacultyAsync();
        var list = await _exam.GetExamTimetableAsync(examId, scope, null, null);
        if (fac != null) list = list.Where(t => t.FacultyId == fac.Id).ToList();   // faculty sees only papers they invigilate
        ViewBag.Exam = exam;
        return View(list);
    }

    [Authorize(Policy = PolicyNames.AnyStaff)]
    public async Task<IActionResult> Marks(int examTimetableId)
    {
        var tt = await _db.ExamTimetables.AsNoTracking().Include(x => x.Exam).Include(x => x.Subject).Include(x => x.Section).Include(x => x.Branch).FirstOrDefaultAsync(x => x.Id == examTimetableId);
        if (tt == null) return NotFound();
        // Faculty can only open marks for papers they invigilate; Admin/HOD can open all.
        if (!_user.IsAdmin && !_user.IsHod)
        {
            var fac = await _user.GetFacultyAsync();
            if (fac == null || tt.FacultyId != fac.Id) return Forbid();
        }
        var marks = await _exam.GetMarksAsync(examTimetableId);
        var regs = await _db.ExamRegistrations.AsNoTracking()
            .Where(r => r.ExamId == tt.ExamId && r.IsEligible)
            .Select(r => r.StudentId)
            .ToListAsync();
        var isExternal = tt.Exam?.Type == "External";
        var lockStatus = marks.FirstOrDefault()?.LockStatus ?? "Draft";
        ViewBag.Timetable = tt;
        ViewBag.EligibleStudentIds = regs;
        ViewBag.IsExternal = isExternal;
        ViewBag.LockStatus = lockStatus;
        ViewBag.PendingQuery = marks.Any(m => m.ModificationQueryStatus == "Pending");
        ViewBag.ModificationStatus = marks.FirstOrDefault(m => m.ModificationQueryStatus != "None")?.ModificationQueryStatus;
        ViewBag.WindowStart = marks.FirstOrDefault()?.ModificationWindowStart;
        ViewBag.WindowEnd = marks.FirstOrDefault()?.ModificationWindowEnd;
        return View(marks);
    }

    [Authorize(Policy = PolicyNames.AnyStaff), HttpPost]
    public async Task<IActionResult> SaveMark(int examTimetableId, int studentId, decimal internalMark, decimal externalMark, string? remarks)
    {
        var user = await _db.Users.FindAsync(_user.UserId);
        var (ok, err) = await _exam.SaveMarkAsync(examTimetableId, studentId, internalMark, externalMark, remarks, user?.FullName);
        TempData[ok ? "Success" : "Error"] = err ?? "Marks saved.";
        return RedirectToAction(nameof(Marks), new { examTimetableId });
    }

    [Authorize(Policy = PolicyNames.AnyStaff), HttpPost]
    public async Task<IActionResult> SubmitMarks(int examTimetableId)
    {
        var fac = await _user.GetFacultyAsync();
        if (fac == null && !_user.IsAdmin) return Forbid();
        var (ok, err) = await _exam.SubmitMarksAsync(examTimetableId, fac?.FullName ?? "Admin");
        TempData[ok ? "Success" : "Error"] = err ?? "Marks submitted for review.";
        return RedirectToAction(nameof(Marks), new { examTimetableId });
    }

    [Authorize(Policy = PolicyNames.AdminOrHod), HttpPost]
    public async Task<IActionResult> LockMarks(int examTimetableId)
    {
        var fac = await _user.GetFacultyAsync();
        var (ok, err) = await _exam.LockMarksAsync(examTimetableId, fac?.FullName ?? "Admin/HOD");
        TempData[ok ? "Success" : "Error"] = err ?? "Marks locked. No further edits allowed.";
        return RedirectToAction(nameof(Marks), new { examTimetableId });
    }

    [Authorize(Policy = PolicyNames.AnyStaff), HttpPost]
    public async Task<IActionResult> RaiseLockQuery(int examTimetableId, string reason)
    {
        var fac = await _user.GetFacultyAsync();
        var (ok, err) = await _exam.RaiseLockQueryAsync(examTimetableId, reason, fac?.FullName ?? "Admin/HOD");
        TempData[ok ? "Success" : "Error"] = err ?? "Modification query raised to HOD/Admin.";
        return RedirectToAction(nameof(Marks), new { examTimetableId });
    }

    // ---------- My raised queries (faculty view) ----------
    /// <summary>Faculty view of modification queries they raised after marks were locked.</summary>
    [Authorize(Policy = PolicyNames.AnyStaff)]
    public async Task<IActionResult> MyQueries()
    {
        var fac = await _user.GetFacultyAsync();
        var facName = fac?.FullName ?? (_user.IsAdmin ? "Admin" : "");
        var q = _db.ExamMarks.AsNoTracking()
            .Include(m => m.ExamTimetable)!.ThenInclude(tt => tt!.Exam)
            .Include(m => m.ExamTimetable)!.ThenInclude(tt => tt!.Subject)
            .Include(m => m.ExamTimetable)!.ThenInclude(tt => tt!.Section)
            .Include(m => m.Student)
            .Where(m => m.ModificationQueryStatus != "None");
        if (!_user.IsAdmin && !_user.IsHod) q = q.Where(m => m.ModificationQueryBy == facName);
        var list = await q.OrderByDescending(m => m.ModificationQueryAt).ToListAsync();
        return View(list);
    }

    // ---------- Modification query review (HOD/Admin) ----------
    [Authorize(Policy = PolicyNames.AdminOrHod)]
    public async Task<IActionResult> LockQueries()
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var list = await _exam.GetLockedMarksAwaitingReviewAsync(scope);
        ViewBag.Scope = scope;
        return View(list);
    }

    [Authorize(Policy = PolicyNames.AdminOrHod), HttpPost]
    public async Task<IActionResult> ReviewLockQuery(int examTimetableId, bool approve, string? note, DateTime? windowStart, DateTime? windowEnd)
    {
        var fac = await _user.GetFacultyAsync();
        var (ok, err) = await _exam.ReviewLockQueryAsync(examTimetableId, approve, note, windowStart, windowEnd, fac?.FullName ?? "Admin/HOD");
        TempData[ok ? "Success" : "Error"] = err ?? (approve ? "Modification window approved." : "Modification query rejected.");
        return RedirectToAction(nameof(LockQueries));
    }

    // ---------- Student/Parent results (gated by ResultsPublished) ----------
    [Authorize(Policy = PolicyNames.StudentOrParent)]
    public async Task<IActionResult> MyResults()
    {
        var uid = _user.UserId;
        if (uid == null) return Forbid();

        // Resolve student by identity user id
        var student = await _db.Students.AsNoTracking().Include(s => s.Branch).Include(s => s.Section)
            .FirstOrDefaultAsync(s => s.IdentityUserId == uid && s.IsActive);
        if (student == null) return View("NoStudent");

        // Parent: resolve by parent record
        if (!User.IsInRole(RoleNames.Student))
        {
            var parent = await _db.Parents.AsNoTracking()
                .Include(p => p.Student!).ThenInclude(s => s.Branch!)
                .Include(p => p.Student!).ThenInclude(s => s.Section!)
                .FirstOrDefaultAsync(p => p.IdentityUserId == uid && p.IsActive);
            if (parent == null || parent.Student == null) return View("NoStudent");
            student = parent.Student;
        }

        var today = DateTime.UtcNow.Date;
        var marks = await _db.ExamMarks.AsNoTracking()
            .Include(m => m.ExamTimetable)!.ThenInclude(et => et!.Exam)
            .Include(m => m.ExamTimetable)!.ThenInclude(et => et!.Subject)
            .Where(m => m.StudentId == student.Id)
            .OrderByDescending(m => m.MarkedAt)
            .ToListAsync();

        // Gate: only show marks for exams whose ResultReleaseDate has passed AND ResultsPublished == true
        var publishedExamIds = await _db.Exams.AsNoTracking()
            .Where(e => e.IsActive && e.ResultsPublished && e.ResultReleaseDate <= DateTime.UtcNow)
            .Select(e => e.Id).ToListAsync();

        var visibleMarks = marks.Where(m => m.ExamTimetable != null && publishedExamIds.Contains(m.ExamTimetable.ExamId)).ToList();

        ViewBag.Student = student;
        ViewBag.Today = today;
        ViewBag.PublishedCount = publishedExamIds.Count;
        return View(visibleMarks);
    }

    // ---------- Admin: manually publish results ----------
    [Authorize(Policy = PolicyNames.AdminOnly), HttpPost]
    public async Task<IActionResult> PublishResults(int examId)
    {
        var exam = await _exam.GetExamAsync(examId);
        if (exam == null) return NotFound();
        if (DateTime.UtcNow < exam.ResultReleaseDate)
        {
            TempData["Error"] = $"Results for {exam.Name} can only be published after {exam.ResultReleaseDate:dd MMM yyyy} (exam end + 3 days).";
            return RedirectToAction(nameof(Index));
        }
        exam.ResultsPublished = true;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Results published for {exam.Name}.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Helpers ----------
    private async Task<List<SelectListItem>> BranchItems(int? scope)
    {
        var q = _db.Branches.AsNoTracking().Where(b => b.IsActive);
        if (scope != null) q = q.Where(b => b.Id == scope);
        return await q.OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name + " (" + b.Code + ")", b.Id.ToString())).ToListAsync();
    }
}

