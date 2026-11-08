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

        if (branchId != null && branchId != -1) q = q.Where(x => x.BranchId == branchId);
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

        if (scope != null && scope != -1) q = q.Where(x => x.BranchId == scope);
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
            .Where(m => true);

        if (!_user.IsAdmin)
        {
            if (scope != null && scope != -1) q = q.Where(m => m.ExamTimetable!.BranchId == scope);
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
        if (marks != null && marks.Count > 10000) return Forbid();
        if (marks != null)
        {
            foreach (var m in marks)
            {
                if (m.Id == 0) continue;
                var dbMark = await _db.ExamMarks.FirstOrDefaultAsync(x => x.Id == m.Id);
                if (dbMark == null) continue;

                if (!ModelState.IsValid) continue;

                bool isStaff = _user.IsAdmin || _user.IsHod;
                bool isOwnerFaculty = false;
                if (!_user.IsAdmin && !_user.IsHod)
                {
                    var ec = await _db.ExamTimetables.AsNoTracking().FirstOrDefaultAsync(e => e.Id == dbMark.ExamTimetableId);
                    var fid = await _db.Faculty.AsNoTracking().Where(f => f.IdentityUserId == _user.UserId).Select(f => f.Id).FirstOrDefaultAsync();
                    isOwnerFaculty = ec != null && ec.FacultyId == fid;
                }

                if (!isStaff && !isOwnerFaculty) continue;
                bool isUnlocked = dbMark.UnlockUntil != null && dbMark.UnlockUntil.Value > DateTime.UtcNow;
                if (dbMark.IsLocked && !isStaff && !isUnlocked) continue;

                if (dbMark.MaxMarks <= 0) continue;
                if (m.MarksObtained < 0 || m.MarksObtained > dbMark.MaxMarks) continue;

                dbMark.MarksObtained = m.MarksObtained;
                dbMark.MaxMarks = m.MaxMarks;
                dbMark.Remarks = m.Remarks;
                dbMark.IsLocked = isStaff ? m.IsLocked : dbMark.IsLocked;
                if (isStaff && !m.IsLocked) dbMark.UnlockUntil = null; // If admin re-locks/bypasses, clear unlock window
                dbMark.LastModifiedBy = _user.DisplayName ?? "Faculty";
                dbMark.LastModifiedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
            TempData["Success"] = "Exam marks updated successfully.";
        }
        return RedirectToAction("Manage");
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.AnyStaff)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestUnlock(long id)
    {
        var mark = await _db.ExamMarks.Include(m => m.ExamTimetable).ThenInclude(e => e!.Faculty).FirstOrDefaultAsync(m => m.Id == id);
        if (mark == null) return NotFound();
        if (!_user.IsAdmin && !_user.IsHod)
        {
            var fid = await _db.Faculty.AsNoTracking().Where(f => f.IdentityUserId == _user.UserId).Select(f => f.Id).FirstOrDefaultAsync();
            if (fid == 0) return Forbid();
            if (mark.ExamTimetable!.FacultyId != fid) return Forbid();
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
        if (mark.ExamTimetable != null)
        {
            if (scope != null && scope != -1 && mark.ExamTimetable.BranchId != scope)
                return Forbid();
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