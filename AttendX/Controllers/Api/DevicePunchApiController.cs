using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AttendX.Controllers.Api;

/// <summary>
/// Biometric / RFID device ingestion endpoint for real-world attendance hardware.
/// A device (attendance machine at the classroom door) posts a punch: { deviceId, studentId, timestamp }.
/// The server converts valid punches (inside the period window, student belongs to the currently-scheduled class)
/// into attendance records exactly as if marked manually — but without touching the web UI.
/// Auth: shared device API token in header X-Device-Token. Configure via AttendX:DeviceApiToken (default "attendx-device-token-change-me").
/// </summary>
[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/devices")]
[AllowAnonymous] // Device uses X-Device-Token, not cookies
public class DevicePunchApiController(AppDbContext db, IClock clock, ISettingsService settings, IAuditService audit, IOptions<AttendXOptions> o) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ISettingsService _settings = settings;
    private readonly IAuditService _audit = audit;
    private readonly AttendXOptions _o = o.Value;

    [HttpPost("punch")]
    public async Task<IActionResult> Punch([FromHeader(Name = "X-Device-Token")] string? deviceToken, [FromBody] PunchRequest req)
    {
        if (string.IsNullOrWhiteSpace(deviceToken) || deviceToken != _o.DeviceApiToken)
            return Unauthorized(new { error = "Invalid device token." });
        if (string.IsNullOrWhiteSpace(req.DeviceId) || req.StudentId <= 0)
            return BadRequest(new { error = "deviceId and studentId are required." });

        var student = await _db.Students.AsNoTracking().Include(s => s.Branch).FirstOrDefaultAsync(s => s.Id == req.StudentId && s.IsActive);
        if (student == null) return NotFound(new { error = "Student not found or inactive." });

        var now = _clock.Now;
        var punchTime = req.Timestamp ?? now;
        var date = punchTime.Date;
        var settingsObj = await _settings.GetAsync();

        // Find today's classes for this student (same branch/section/semester)
        var daySchedule = await _db.Timetables.AsNoTracking()
            .Include(t => t.Subject).Include(t => t.Section).Include(t => t.Faculty)
            .Where(t => t.IsActive && t.Day == date.DayOfWeek && t.BranchId == student.BranchId && t.SectionId == student.SectionId && t.Semester == student.Semester)
            .ToListAsync();

        var grace = TimeSpan.FromMinutes(settingsObj.GraceMinutes);
        var period = daySchedule.FirstOrDefault(t => punchTime.TimeOfDay >= t.StartTime - grace && punchTime.TimeOfDay <= t.EndTime + grace);
        if (period == null)
        {
            // Outside every period window (including grace) — find nearest for diagnostics
            period = daySchedule.OrderBy(t => Math.Abs((t.StartTime - punchTime.TimeOfDay).TotalMinutes)).FirstOrDefault();
            if (period == null) return NotFound(new { error = "No class scheduled for this student today." });
            return BadRequest(new { error = $"Punch at {punchTime:HH:mm} is outside the period window (P{period.Period})." });
        }

        // Dedup: if attendance already exists for this student/time/period/subject, report it
        bool exists = await _db.Attendance.AnyAsync(a => a.StudentId == student.Id && a.Date == date && a.Period == period.Period && a.SubjectId == period.SubjectId);
        if (exists)
            return Ok(new { status = "AlreadyMarked", studentId = student.Id, studentIdClean = student.StudentID, subject = period.Subject?.SubjectCode, period = period.Period });

        // Determine status
        AttendanceStatus status = punchTime.TimeOfDay <= period.StartTime + TimeSpan.FromMinutes(settingsObj.LateAfterMinutes)
            ? AttendanceStatus.Present
            : AttendanceStatus.Late;

        var session = await _db.AttendanceSessions.FirstOrDefaultAsync(s => s.TimetableId == period.Id && s.Date == date);
        if (session == null)
        {
            session = new AttendanceSession
            {
                TimetableId = period.Id, AcademicYearId = period.AcademicYearId, Date = date, Period = period.Period,
                SubjectId = period.SubjectId, SectionId = period.SectionId, FacultyId = period.FacultyId,
                Mode = "Device", DeviceId = req.DeviceId, StartedAt = punchTime, StartedByUserId = "Device:" + req.DeviceId
            };
            _db.AttendanceSessions.Add(session);
            await _db.SaveChangesAsync();
        }

        _db.Attendance.Add(new Attendance
        {
            StudentId = student.Id, SubjectId = period.SubjectId, FacultyId = period.FacultyId, BranchId = student.BranchId,
            SectionId = student.SectionId, Semester = student.Semester, Date = date, Period = period.Period,
            Status = status, MarkedAt = punchTime, AttendanceSessionId = session.Id, Source = "Device", DeviceId = req.DeviceId
        });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("DevicePunch", "Attendance", student.Id.ToString(), actorName: "Device:" + req.DeviceId, details: $"{status} period {period.Period}");

        return Ok(new { status = "Marked", studentId = student.Id, studentIdClean = student.StudentID, subject = period.Subject?.SubjectCode, period = period.Period, attendanceStatus = status.ToString(), deviceId = req.DeviceId });
    }

    private static int GetCurrentPeriod(DateTime time, Timetable t) => t.Period;
}

public class PunchRequest
{
    public string DeviceId { get; set; } = "";
    public int StudentId { get; set; }
    public DateTime? Timestamp { get; set; }
}
