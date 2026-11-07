using AttendX.Data;
using AttendX.Infrastructure;
using AttendX.Models;
using AttendX.Services;
using AttendX.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendX.Tests;

internal class FakeClock : IClock { public DateTime Now { get; set; } = new(2026, 10, 12, 10, 20, 0); }   // Monday 10:20

internal class FakeUser : ICurrentUser
{
    public Faculty? Faculty { get; set; }
    public bool IsAuthenticated => true;
    public string? UserId => "u1";
    public string DisplayName => "tester";
    public bool IsAdmin { get; set; }
    public bool IsHod { get; set; }
    public bool IsFaculty { get; set; } = true;
    public string? IpAddress => "127.0.0.1";
    public Task<Faculty?> GetFacultyAsync() => Task.FromResult(Faculty);
    public Task<int?> GetScopeBranchIdAsync() => Task.FromResult<int?>(IsAdmin ? null : Faculty?.BranchId ?? -1);
}

internal class FakeQr : IQrService
{
    public QrSigner Signer { get; } = new("test-secret");
    public byte[] RenderPng(string payload, int pixelsPerModule = 10) => Array.Empty<byte>();
    public Task<string> GenerateStudentQrAsync(Student student) => Task.FromResult("QRCodes/x.png");
    public string GetStudentPayload(Student s) => Signer.BuildStudentPayload(s.Id, s.StudentID);
}

public class CalendarServiceTests
{
    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Sunday_holiday_and_normal_day()
    {
        var db = NewDb();
        db.Holidays.Add(new Holiday { Date = new DateTime(2026, 10, 14), Name = "Diwali break", Type = "Holiday" });
        db.Holidays.Add(new Holiday { Date = new DateTime(2026, 10, 15), Name = "Tech fest", Type = "Event" });
        await db.SaveChangesAsync();
        var cal = new CalendarService(db, new FakeClock());
        Assert.Equal("Sunday", await cal.GetNonWorkingReasonAsync(new DateTime(2026, 10, 18)));
        Assert.Equal("Diwali break", await cal.GetNonWorkingReasonAsync(new DateTime(2026, 10, 14)));
        Assert.Null(await cal.GetNonWorkingReasonAsync(new DateTime(2026, 10, 15)));   // events do not block
        Assert.Null(await cal.GetNonWorkingReasonAsync(new DateTime(2026, 10, 13)));
    }
}

public class AttendanceServiceTests
{
    private readonly AppDbContext _db;
    private readonly FakeClock _clock = new();
    private readonly FakeUser _user = new();
    private readonly FakeQr _qr = new();
    private readonly AttendanceService _svc;
    private readonly Faculty _fac, _other;
    private readonly Timetable _tt;
    private readonly List<Student> _students = new();
    private readonly Student _outsider;

    public AttendanceServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(opts);
        var year = new AcademicYear { Name = "2026-27", StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2027, 5, 31), IsCurrent = true };
        var branch = new Branch { Name = "CSE", Code = "CSE" };
        var sec = new Section { Name = "EV1", Branch = branch }; var sec2 = new Section { Name = "EV2", Branch = branch };
        var subj = new Subject { SubjectCode = "CSE-501", SubjectName = "TOC", Branch = branch, Semester = 5, Credits = 4 };
        _fac = new Faculty { FacultyID = "F1", FullName = "Teacher One", Email = "t1@x", Phone = "1", Branch = branch, Designation = "Professor", IdentityUserId = "u1" };
        _other = new Faculty { FacultyID = "F2", FullName = "Teacher Two", Email = "t2@x", Phone = "1", Branch = branch, Designation = "Professor" };
        _db.AddRange(year, branch, sec, sec2, subj, _fac, _other);
        _db.SaveChanges();
        _tt = new Timetable { Branch = branch, Section = sec, Semester = 5, Day = DayOfWeek.Monday, Period = 2, Subject = subj, Faculty = _fac, RoomNo = "A1", StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(10, 55, 0), IsActive = true, AcademicYearId = year.Id };
        _db.Timetables.Add(_tt);
        for (int i = 1; i <= 3; i++) _students.Add(new Student { StudentID = $"5EV1-CSE-00{i}", FullName = $"Student {i}", Email = $"s{i}@x", Phone = "1", Branch = branch, Section = sec, Semester = 5, Year = 3 });
        _outsider = new Student { StudentID = "5EV2-CSE-001", FullName = "Outsider", Email = "o@x", Phone = "1", Branch = branch, Section = sec2, Semester = 5, Year = 3 };
        _db.Students.AddRange(_students); _db.Students.Add(_outsider);
        _db.SaveChanges();

        _user.Faculty = _fac;
        var settings = new SettingsService(_db, Options.Create(new AttendXOptions()));
        _svc = new AttendanceService(_db, _clock, _user, settings, _qr, new AuditService(_db, _user), new CalendarService(_db, _clock));
    }

    private async Task<string> Token() => (await _svc.IssueSessionTokenAsync(_tt.Id)).Token!;
    private static readonly DeviceInfo Dev = new("dev1", null, null);

    [Fact]
    public async Task Assigned_faculty_can_mark_manual_attendance_inside_window()
    {
        var map = _students.ToDictionary(s => s.Id, s => s.Id == _students[0].Id ? "Absent" : "Present");
        map[_outsider.Id] = "Present";                          // student of another section must be ignored
        var r = await _svc.SaveManualAsync(_tt.Id, _clock.Now.Date, map, null, Dev);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(3, r.Added);
        Assert.Equal(3, await _db.Attendance.CountAsync());
        Assert.DoesNotContain(await _db.Attendance.ToListAsync(), a => a.StudentId == _outsider.Id);
    }

    [Fact]
    public async Task Faculty_can_mark_or_make_changes_anytime_during_the_entire_day()
    {
        _clock.Now = new DateTime(2026, 10, 12, 13, 0, 0);
        var r = await _svc.SaveManualAsync(_tt.Id, _clock.Now.Date, _students.ToDictionary(s => s.Id, _ => "Present"), null, Dev);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(3, await _db.Attendance.CountAsync());
    }

    [Fact]
    public async Task Another_faculty_cannot_mark_this_class()
    {
        _user.Faculty = _other;
        var r = await _svc.SaveManualAsync(_tt.Id, _clock.Now.Date, _students.ToDictionary(s => s.Id, _ => "Present"), null, Dev);
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Admin_correction_after_period_needs_a_reason_and_is_audited()
    {
        await _svc.SaveManualAsync(_tt.Id, _clock.Now.Date, _students.ToDictionary(s => s.Id, _ => "Present"), null, Dev);
        _clock.Now = new DateTime(2026, 10, 12, 15, 0, 0);
        _user.IsAdmin = true; _user.IsFaculty = false; _user.Faculty = null;
        var change = new Dictionary<int, string> { [_students[0].Id] = "Absent" };

        var noReason = await _svc.SaveManualAsync(_tt.Id, _clock.Now.Date, change, " ", Dev);
        Assert.False(noReason.Ok);

        var ok = await _svc.SaveManualAsync(_tt.Id, _clock.Now.Date, change, "Marked wrongly by mistake", Dev);
        Assert.True(ok.Ok, ok.Message);
        Assert.Equal(1, ok.Updated);
        var audit = await _db.AuditLogs.FirstAsync(a => a.Action == "AttendanceCorrected");
        Assert.Equal("Present", audit.OldValues); Assert.Equal("Absent", audit.NewValues); Assert.Equal("Marked wrongly by mistake", audit.Reason);
    }

    [Fact]
    public async Task Qr_scan_marks_once_and_second_scan_is_duplicate()
    {
        var token = await Token();
        var payload = _qr.GetStudentPayload(_students[0]);
        var first = await _svc.ScanAsync(_tt.Id, payload, token, Dev);
        var second = await _svc.ScanAsync(_tt.Id, payload, token, Dev);
        Assert.Equal(ScanCode.Marked, first.Code);
        Assert.Equal(ScanCode.AlreadyMarked, second.Code);
        Assert.Equal(1, await _db.Attendance.CountAsync());
    }

    [Fact]
    public async Task Qr_scan_rejects_tampered_wrong_class_and_expired_session()
    {
        var token = await Token();
        var good = _qr.GetStudentPayload(_students[0]);
        Assert.Equal(ScanCode.InvalidQr, (await _svc.ScanAsync(_tt.Id, good.Replace("AX1.", "AX1.9"), token, Dev)).Code);
        Assert.Equal(ScanCode.WrongClass, (await _svc.ScanAsync(_tt.Id, _qr.GetStudentPayload(_outsider), token, Dev)).Code);
        Assert.Equal(ScanCode.SessionExpired, (await _svc.ScanAsync(_tt.Id, good, "not-a-valid-token", Dev)).Code);

        _clock.Now = _clock.Now.AddMinutes(1);                  // token of an old 30 s window is now stale
        Assert.Equal(ScanCode.SessionExpired, (await _svc.ScanAsync(_tt.Id, good, token, Dev)).Code);
        Assert.Equal(0, await _db.Attendance.CountAsync());
    }

    [Fact]
    public async Task Qr_scan_after_the_late_limit_is_stored_as_late()
    {
        _clock.Now = new DateTime(2026, 10, 12, 10, 30, 0);     // > start + 15 min
        var token = await Token();
        var r = await _svc.ScanAsync(_tt.Id, _qr.GetStudentPayload(_students[1]), token, Dev);
        Assert.Equal(ScanCode.Marked, r.Code);
        Assert.Equal(AttendanceStatus.Late, (await _db.Attendance.FirstAsync()).Status);
    }

    [Fact]
    public async Task Offline_records_are_revalidated_on_sync()
    {
        var captured = new DateTime(2026, 10, 12, 10, 15, 0, DateTimeKind.Local);
        var good = new OfflineRecord { ClientId = "a", TimetableId = _tt.Id, Date = "2026-10-12", CapturedAt = captured, StudentId = _students[0].Id, Status = "Present" };
        var wrongClass = new OfflineRecord { ClientId = "b", TimetableId = _tt.Id, Date = "2026-10-12", CapturedAt = captured, StudentId = _outsider.Id, Status = "Present" };
        var tooEarly = new OfflineRecord { ClientId = "c", TimetableId = _tt.Id, Date = "2026-10-12", CapturedAt = captured.AddHours(-3), StudentId = _students[1].Id, Status = "Present" };
        var res = await _svc.SyncOfflineAsync(new List<OfflineRecord> { good, wrongClass, tooEarly, good });
        Assert.True(res[0].Accepted);
        Assert.False(res[1].Accepted);
        Assert.False(res[2].Accepted);
        Assert.False(res[3].Accepted);                          // duplicate
        Assert.Equal(1, await _db.Attendance.CountAsync());
    }

    [Fact]
    public async Task Marking_is_blocked_on_a_holiday()
    {
        _db.Holidays.Add(new Holiday { Date = _clock.Now.Date, Name = "Test Holiday", Type = "Holiday" });
        await _db.SaveChangesAsync();
        var r = await _svc.SaveManualAsync(_tt.Id, _clock.Now.Date, _students.ToDictionary(s => s.Id, _ => "Present"), null, Dev);
        Assert.False(r.Ok);
        Assert.Contains("Test Holiday", r.Message);
        Assert.Empty(await _svc.GetSlotsAsync(_clock.Now.Date, null, null, null));
    }
}
