using AttendX.ViewModels;

namespace AttendX.Controllers;

public class ExamController(AppDbContext db, ICurrentUser user) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;

    [Authorize]
    public async Task<IActionResult> Index(string examType, int? semester)
    {
        return await Results(examType, semester);
    }

    [Authorize]
    public async Task<IActionResult> Results(string examType, int? semester)
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
        var examIds = exams.Select(e => e.Id).ToList();
        var marks = await _db.ExamMarks.AsNoTracking()
            .Where(m => m.StudentId == me.Id && examIds.Contains(m.ExamTimetableId))
            .ToDictionaryAsync(m => m.ExamTimetableId, m => m);

        var viewModel = exams.Select(e => new ExamResultViewModel
        {
            Exam = e,
            Mark = marks.GetValueOrDefault(e.Id)
        }).ToList();

        ViewBag.Semester = sem;
        ViewBag.ExamType = examType;
        ViewBag.IsStudent = true;
        return View("Index", viewModel);
    }

    [Authorize]
    public async Task<IActionResult> Timetable(string examType, int? semester)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        bool isStudentOnly = !_user.IsAdmin && !_user.IsHod && !_user.IsFaculty;
        int? branchId = scope;
        int? mySem = semester;

        if (isStudentOnly)
        {
            var me = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.IdentityUserId == _user.UserId && s.IsActive);
            if (me == null) return NotFound();
            branchId = me.BranchId;
            mySem = semester ?? me.Semester;
        }

        var q = _db.ExamTimetables.AsNoTracking()
            .Include(x => x.Subject).Include(x => x.Faculty).Include(x => x.AcademicYear).Include(x => x.Branch)
            .Where(x => x.IsActive);

        // branchId is null only for Admin (unrestricted). -1 means HOD/Faculty with no department → matches nothing.
        if (branchId != null) q = q.Where(x => x.BranchId == branchId);
        if (mySem != null) q = q.Where(x => x.Semester == mySem);
        if (!string.IsNullOrEmpty(examType)) q = q.Where(x => x.ExamType == examType);

        var exams = await q.OrderBy(x => x.ExamDate).ToListAsync();
        ViewBag.Semester = mySem;
        ViewBag.ExamType = examType;
        ViewBag.BranchId = branchId;
        ViewBag.Branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).Select(b => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(b.Name, b.Id.ToString())).ToListAsync();
        return View("Index", exams.Select(e => new ExamResultViewModel { Exam = e, Mark = null }).ToList());
    }

    [Authorize(Policy = PolicyNames.AdminOrHod)]
    public async Task<IActionResult> Admin(int? semester, int? branchId, string examType, int? yearId)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var q = _db.ExamTimetables.AsNoTracking()
            .Include(x => x.Subject).Include(x => x.Faculty).Include(x => x.AcademicYear).Include(x => x.Branch)
            .Where(x => x.IsActive);

        // scope is null only for Admin (unrestricted). A HOD/Faculty with no department gets -1 which matches nothing — never widen.
        if (scope.HasValue) q = q.Where(x => x.BranchId == scope);
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

    [Authorize(Policy = PolicyNames.AnyStaff)]
    public async Task<IActionResult> Manage(int? examTimetableId, int? branchId, int? semester, string examType)
    {
        var scope = await _user.GetScopeBranchIdAsync();
        var q = _db.ExamMarks.AsNoTracking()
            .Include(m => m.ExamTimetable).ThenInclude(e => e!.Subject)
            .Include(m => m.ExamTimetable).ThenInclude(e => e!.Branch)
            .Include(m => m.Student)
            .Where(m => m.Id != 0);

        if (!_user.IsAdmin)
        {
            if (scope != null)
            {
                q = q.Where(m => m.ExamTimetable!.BranchId == scope);
            }
            else
            {
                var fid = await _db.Faculty.AsNoTracking().Where(f => f.IdentityUserId == _user.UserId).Select(f => f.Id).FirstOrDefaultAsync();
                if (fid == 0) return Forbid();
                q = q.Where(m => m.ExamTimetable!.FacultyId == fid);
            }
        }

        if (examTimetableId != null) q = q.Where(m => m.ExamTimetableId == examTimetableId);
        if (branchId != null) q = q.Where(m => m.ExamTimetable!.BranchId == branchId);
        if (semester != null) q = q.Where(m => m.ExamTimetable!.Semester == semester);
        if (!string.IsNullOrEmpty(examType)) q = q.Where(m => m.ExamTimetable!.ExamType == examType);

        var marks = await q.OrderBy(m => m.ExamTimetable!.ExamDate).ThenBy(m => m.Student!.FullName).ToListAsync();

        ViewBag.ExamTimetableId = examTimetableId;
        ViewBag.BranchId = branchId;
        ViewBag.Semester = semester;
        ViewBag.ExamType = examType;
        ViewBag.CanModify = _user.IsAdmin || _user.IsHod;
        ViewBag.Exams = await _db.ExamTimetables.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.ExamDate).Select(e => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem($"{e.Subject!.SubjectCode} - {e.ExamType} ({e.ExamDate:dd MMM})", e.Id.ToString())).ToListAsync();
        ViewBag.Branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Name).Select(b => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(b.Name, b.Id.ToString())).ToListAsync();

        return View(marks);
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AnyStaff)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMarks(List<ExamMark> marks)
    {
        if (marks == null) return RedirectToAction("Manage");
        if (marks.Count > 10000) return BadRequest("Too many rows.");

        var isAdmin = _user.IsAdmin;
        var isHod = _user.IsHod;
        var scope = await _user.GetScopeBranchIdAsync();
        int? myFacultyId = null;
        if (!isAdmin && !isHod)
            myFacultyId = await _db.Faculty.AsNoTracking().Where(f => f.IdentityUserId == _user.UserId).Select(f => f.Id).FirstOrDefaultAsync();

        var ids = marks.Where(m => m.Id > 0).Select(m => m.Id).ToList();
        if (ids.Count == 0) return RedirectToAction("Manage");

        var dbMarks = await _db.ExamMarks.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var timetableIds = dbMarks.Values.Select(x => x.ExamTimetableId).Distinct().ToList();
        var timetables = await _db.ExamTimetables.AsNoTracking().Where(e => timetableIds.Contains(e.Id))
            .Select(e => new { e.Id, e.BranchId, e.FacultyId }).ToDictionaryAsync(e => e.Id);

        int updated = 0, skipped = 0;
        foreach (var m in marks)
        {
            if (m.Id == 0 || !dbMarks.TryGetValue(m.Id, out var dbMark)) { skipped++; continue; }
            if (!timetables.TryGetValue(dbMark.ExamTimetableId, out var et)) { skipped++; continue; }

            bool isHodInScope = isHod && scope.HasValue && et.BranchId == scope.Value;
            bool isOwnerFaculty = myFacultyId.HasValue && et.FacultyId == myFacultyId.Value;
            if (!isAdmin && !isHodInScope && !isOwnerFaculty) { skipped++; continue; }

            bool canModify = isAdmin || isHodInScope;
            bool isUnlocked = dbMark.UnlockUntil != null && dbMark.UnlockUntil.Value > DateTime.UtcNow;
            if (dbMark.IsLocked && !canModify && !isUnlocked) { skipped++; continue; }

            if (m.MaxMarks <= 0 || m.MaxMarks > 1000) { skipped++; continue; }
            if (m.MarksObtained < 0 || m.MarksObtained > m.MaxMarks) { skipped++; continue; }

            dbMark.MarksObtained = m.MarksObtained;
            dbMark.MaxMarks = m.MaxMarks;
            dbMark.Remarks = m.Remarks;
            dbMark.IsLocked = canModify ? m.IsLocked : dbMark.IsLocked;
            if (canModify && !m.IsLocked) dbMark.UnlockUntil = null;
            dbMark.LastModifiedBy = _user.DisplayName ?? "Faculty";
            dbMark.LastModifiedAt = DateTime.UtcNow;
            updated++;
        }
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Exam marks updated: {updated} saved, {skipped} skipped.";
        return RedirectToAction("Manage");
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AnyStaff)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestUnlock(long id)
    {
        var mark = await _db.ExamMarks.Include(m => m.ExamTimetable).ThenInclude(e => e!.Faculty).FirstOrDefaultAsync(m => m.Id == id);
        if (mark == null) return NotFound();
        // HOD must be within their own department's scope, or be the owning faculty.
        var scopeU = await _user.GetScopeBranchIdAsync();
        if (_user.IsHod && scopeU.HasValue)
        {
            if (mark.ExamTimetable?.BranchId != scopeU.Value) return Forbid();
        }
        else if (!_user.IsAdmin && !_user.IsHod)
        {
            var fid = await _db.Faculty.AsNoTracking().Where(f => f.IdentityUserId == _user.UserId).Select(f => f.Id).FirstOrDefaultAsync();
            if (fid == 0) return Forbid();
            if (mark.ExamTimetable?.FacultyId != fid) return Forbid();
        }
        else if (_user.IsHod)
        {
            // HOD with no department scope must not touch other branches' marks
            return Forbid();
        }
        if (mark.ExamTimetable?.FacultyId == 0) return Forbid();
        if (mark.UnlockRequestedAt != null) return RedirectToAction("Manage");
        mark.UnlockRequestedAt = DateTime.UtcNow;
        mark.LastModifiedBy = _user.DisplayName ?? "Faculty";
        mark.LastModifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Unlock request sent to HOD/Admin.";
        return RedirectToAction("Manage");
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AdminOrHod)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GrantUnlock(long id, int hours = 24)
    {
        var mark = await _db.ExamMarks.Include(m => m.ExamTimetable).FirstOrDefaultAsync(m => m.Id == id);
        if (mark == null) return NotFound();
        var scope = await _user.GetScopeBranchIdAsync();
        // null scope => Admin (unrestricted). A scoped non-admin must match their branch.
        if (!_user.IsAdmin)
        {
            if (mark.ExamTimetable == null) return Forbid();
            if (scope == null) return Forbid();
            if (mark.ExamTimetable.BranchId != scope.Value) return Forbid();
        }
        if (hours < 1 || hours > 72) return BadRequest("Hours must be 1-72.");
        if (!mark.IsLocked && mark.UnlockUntil == null) return NotFound();

        mark.IsLocked = false;
        mark.UnlockUntil = DateTime.UtcNow.AddHours(hours);
        mark.UnlockRequestedAt = null;
        mark.LastModifiedBy = _user.DisplayName ?? "Admin";
        mark.LastModifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Marked unlocked for {hours} hours.";
        return RedirectToAction("Manage");
    }
}