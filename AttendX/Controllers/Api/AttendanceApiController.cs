using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendX.Controllers.Api;

/// <summary>REST API for attendance operations. Cookie auth (same as web UI). Pass RequestVerificationToken header for POST.</summary>
[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/attendance")]
[Authorize]
public class AttendanceApiController(IAttendanceService attendance, ICurrentUser user, AppDbContext db) : ControllerBase
{
    private readonly IAttendanceService _attendance = attendance;
    private readonly ICurrentUser _user = user;
    private readonly AppDbContext _db = db;

    /// <summary>List today's class slots for the signed-in faculty (or all if admin).</summary>
    [HttpGet("slots")]
    public async Task<IActionResult> GetSlots([FromQuery] DateTime? date)
    {
        var d = (date ?? DateTime.UtcNow).Date;
        var fac = await _user.GetFacultyAsync();
        var slots = await _attendance.GetSlotsAsync(d, fac?.Id, null, null);
        return Ok(slots);
    }

    /// <summary>Submit attendance for a class.</summary>
    [HttpPost("mark")]
    public async Task<IActionResult> Mark([FromBody] MarkRequest req)
    {
        var result = await _attendance.SaveManualAsync(req.TimetableId, DateTime.UtcNow.Date, req.Statuses, req.Reason,
            new DeviceInfo(req.DeviceId, req.Latitude, req.Longitude));
        if (!result.Ok) return BadRequest(new { error = result.Message });
        return Ok(result);
    }

    /// <summary>Get a single class's mark sheet (for API clients).</summary>
    [HttpGet("markpage/{timetableId:int}")]
    public async Task<IActionResult> GetMarkPage(int timetableId, [FromQuery] DateTime? date)
    {
        var (page, error) = await _attendance.BuildMarkPageAsync(timetableId, (date ?? DateTime.UtcNow).Date);
        if (error != null) return NotFound(new { error });
        return Ok(page);
    }
}

public record MarkRequest(int TimetableId, Dictionary<int, string> Statuses, string? Reason, string? DeviceId, decimal? Latitude, decimal? Longitude);
