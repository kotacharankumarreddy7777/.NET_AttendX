using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendX.Controllers.Api;

/// <summary>REST API for exam operations: upcoming exams, results, eligibility.</summary>
[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/exams")]
[Authorize]
public class ExamApiController(AppDbContext db, ICurrentUser user, IExamService examSvc) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly IExamService _exam = examSvc;

    /// <summary>List all active exams.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var list = await _exam.GetExamsAsync();
        return Ok(list.Select(e => new { e.Id, e.Name, e.Type, e.Semester, e.StartDate, e.EndDate }));
    }

    /// <summary>Get exam timetable (optionally filtered by examId).</summary>
    [HttpGet("timetable")]
    public async Task<IActionResult> Timetable([FromQuery] int? examId)
    {
        var list = await _exam.GetExamTimetableAsync(examId, null, null, null);
        return Ok(list.Select(t => new
        {
            t.Id, examId = t.ExamId, t.SubjectId, t.BranchId, t.SectionId, t.Semester,
            t.ExamDate, t.StartTime, t.EndTime, t.RoomNo, t.FacultyId
        }));
    }

    /// <summary>Student: get own results (cookie auth).</summary>
    [HttpGet("my-results")]
    [Authorize(Policy = PolicyNames.StudentOnly)]
    public async Task<IActionResult> MyResults()
    {
        var me = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.IdentityUserId == _user.UserId);
        if (me == null) return NotFound();
        var rows = await _db.ExamMarks.AsNoTracking()
            .Include(m => m.ExamTimetable)!.ThenInclude(tt => tt!.Exam)
            .Include(m => m.ExamTimetable)!.ThenInclude(tt => tt!.Subject)
            .Where(m => m.StudentId == me.Id)
            .OrderByDescending(m => m.MarkedAt)
            .ToListAsync();
        return Ok(rows.Select(m => new
        {
            exam = m.ExamTimetable?.Exam?.Name,
            subject = m.ExamTimetable?.Subject?.SubjectCode,
            internalMark = m.InternalMark,
            externalMark = m.ExternalMark,
            total = m.TotalMark,
            m.Grade,
            isExternal = m.ExamTimetable?.Exam?.Type == "External"
        }));
    }

    /// <summary>Student: check own exam eligibility (attendance gate).</summary>
    [HttpGet("my-eligibility")]
    [Authorize(Policy = PolicyNames.StudentOrParent)]
    public async Task<IActionResult> MyEligibility()
    {
        var uid = _user.UserId;
        int? studentId = null;
        if (User.IsInRole(RoleNames.Student))
        {
            var me = await _db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.IdentityUserId == uid);
            studentId = me?.Id;
        }
        else if (User.IsInRole(RoleNames.Parent))
        {
            var p = await _db.Parents.AsNoTracking().FirstOrDefaultAsync(x => x.IdentityUserId == uid);
            studentId = p?.StudentId;
        }
        if (studentId == null) return NotFound();
        var regs = await _db.ExamRegistrations.AsNoTracking()
            .Include(r => r.Exam)
            .Where(r => r.StudentId == studentId)
            .OrderBy(r => r.Exam!.StartDate)
            .ToListAsync();
        return Ok(regs.Select(r => new { exam = r.Exam?.Name, r.IsEligible, r.EligibilityReason, r.HallTicketGenerated }));
    }
}
