using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendX.Controllers.Api;

/// <summary>REST API for student operations. Cookie auth (same as web UI). Students see only their own data; Admin/HOD see all.</summary>
[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/students")]
[Authorize]
public class StudentsApiController(AppDbContext db, ICurrentUser user) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;

    /// <summary>List students (Admin/HOD only). Filter by branch/section/semester.</summary>
    [HttpGet]
    [Authorize(Policy = PolicyNames.AdminOrHod)]
    public async Task<IActionResult> List([FromQuery] int? branchId, [FromQuery] int? sectionId, [FromQuery] int? semester, [FromQuery] string? search)
    {
        var q = _db.Students.AsNoTracking().Where(s => s.IsActive);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);
        if (sectionId.HasValue) q = q.Where(s => s.SectionId == sectionId);
        if (semester.HasValue) q = q.Where(s => s.Semester == semester);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(s => s.FullName.Contains(search) || s.StudentID.Contains(search));
        var list = await q.OrderBy(s => s.StudentID).Take(500)
            .Select(s => new { s.Id, s.StudentID, s.FullName, s.Semester, s.Year, s.BranchId, s.SectionId, s.Email, s.Phone })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Get the signed-in student's profile.</summary>
    [HttpGet("me")]
    [Authorize(Policy = PolicyNames.StudentOnly)]
    public async Task<IActionResult> Me()
    {
        var me = await _db.Students.AsNoTracking()
            .Include(s => s.Branch).Include(s => s.Section)
            .FirstOrDefaultAsync(s => s.IdentityUserId == _user.UserId);
        if (me == null) return NotFound();
        return Ok(new
        {
            me.Id, me.StudentID, me.FullName, me.Semester, me.Year, me.Email, me.Phone,
            branch = me.Branch?.Name, section = me.Section?.Name,
            joiningDate = me.JoiningDate.ToString("yyyy-MM-dd")
        });
    }
}
