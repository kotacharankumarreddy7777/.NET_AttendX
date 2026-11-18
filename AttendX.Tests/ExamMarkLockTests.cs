using AttendX.Data;
using AttendX.Models;
using AttendX.Services;
using AttendX.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendX.Tests;

/// <summary>
/// Integration-style tests for the exam mark lock / modification workflow using the InMemory provider.
/// Note: these exercise the service layer only, not HTTP antiforgery.
/// </summary>
public class ExamMarkLockTests
{
    private static AppDbContext NewDb(string name) => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);

    private sealed class FakeUser : ICurrentUser
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
        public Task<int?> GetScopeBranchIdAsync() => Task.FromResult<int?>(Faculty?.BranchId);
    }

    private sealed class FakeSettings(AppDbContext db) : ISettingsService
    {
        public Task<AttendanceSettings> GetAsync()
            => Task.FromResult(new AttendanceSettings { DefaultThreshold = 75m, LateWeight = 1m, PeriodCount = 7 });
        public Task SaveAsync(AttendanceSettings s) => Task.CompletedTask;
        public async Task<AcademicYear> GetCurrentYearAsync()
            => await db.AcademicYears.FirstOrDefaultAsync() ?? new AcademicYear { Name = "2026-27", StartDate = new DateTime(2026, 6, 1), EndDate = new DateTime(2027, 5, 31), IsCurrent = true };
        public Task<Semester?> GetCurrentSemesterAsync()
            => Task.FromResult<Semester?>(db.Semesters.FirstOrDefault(s => s.IsCurrent) ?? new Semester { Number = 1, Type = "Odd", StartDate = new DateTime(2026, 8, 1), EndDate = new DateTime(2026, 12, 15), ExamStartDate = new DateTime(2026, 12, 16), ExamEndDate = new DateTime(2026, 12, 30), IsCurrent = true });
    }

    private sealed class FakeAttendance : IAttendanceService
    {
        public Task<List<SlotView>> GetSlotsAsync(DateTime date, int? facultyId, int? branchId, int? sectionId) => throw new NotImplementedException();
        public Task<AccessCheck> CheckAccessAsync(Timetable t, DateTime date) => throw new NotImplementedException();
        public Task<(MarkPageViewModel? Page, string? Error)> BuildMarkPageAsync(int timetableId, DateTime date) => throw new NotImplementedException();
        public Task<SaveResult> SaveManualAsync(int timetableId, DateTime date, Dictionary<int, string> statuses, string? reason, DeviceInfo device) => throw new NotImplementedException();
        public Task<(string? Token, int ExpiresIn, string? Error)> IssueSessionTokenAsync(int timetableId) => throw new NotImplementedException();
        public Task<ScanResult> ScanAsync(int timetableId, string payload, string token, DeviceInfo device) => throw new NotImplementedException();
        public Task<List<SyncResult>> SyncOfflineAsync(List<OfflineRecord> records) => throw new NotImplementedException();
        public Task<int> MarkAbsentAfterGracePeriodAsync() => throw new NotImplementedException();
    }

    private static (AppDbContext Db, ExamTimetable Paper) Setup(string dbName)
    {
        var db = NewDb(dbName + "-" + Guid.NewGuid());
        var branch = new Branch { Name = "CSE", Code = "CSE" };
        var sec = new Section { Name = "EV1", Branch = branch };
        var subj = new Subject { SubjectCode = "CSE-501", SubjectName = "TOC", Branch = branch, Semester = 5, Credits = 4 };
        var fac = new Faculty { FacultyID = "F1", FullName = "Teacher One", Email = "t1@x", Phone = "1", Branch = branch, Designation = "Professor", IdentityUserId = "u1" };
        db.AddRange(branch, sec, subj, fac);
        db.SaveChanges();
        var exam = new Exam { Name = "End-Semester", Type = "External", Semester = 5, AcademicYearId = 1, StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 15), IsActive = true };
        db.Exams.Add(exam);
        db.SaveChanges();
        var paper = new ExamTimetable
        {
            ExamId = exam.Id, SubjectId = subj.Id, BranchId = branch.Id, SectionId = sec.Id, Semester = 5,
            FacultyId = fac.Id, ExamDate = new DateTime(2026, 12, 16), StartTime = TimeSpan.FromHours(10), EndTime = TimeSpan.FromHours(13), RoomNo = "A-101"
        };
        db.ExamTimetables.Add(paper);
        db.SaveChanges();
        return (db, paper);
    }

    [Fact]
    public async Task Locked_paper_rejects_further_edits_unless_inside_approved_window()
    {
        var (db, paper) = Setup("lock-flow");
        var user = new FakeUser { Faculty = await db.Faculty.FirstAsync() };
        var svc = new ExamService(db, user, new FakeSettings(db), new FakeAttendance());

        // Enter + submit + lock
        var (saveOk, saveErr) = await svc.SaveMarkAsync(paper.Id, 1, 25m, 60m, null, "T");
        Assert.True(saveOk, saveErr);
        var (submitOk, submitErr) = await svc.SubmitMarksAsync(paper.Id, "T");
        Assert.True(submitOk, submitErr);
        var (lockOk, lockErr) = await svc.LockMarksAsync(paper.Id, "HOD");
        Assert.True(lockOk, lockErr);

        // Save after lock must fail
        var (save2Ok, save2Err) = await svc.SaveMarkAsync(paper.Id, 1, 20m, 0m, null, "T");
        Assert.False(save2Ok);
        Assert.Contains("locked", save2Err ?? "", StringComparison.OrdinalIgnoreCase);

        // Faculty raises query
        var (qOk, qErr) = await svc.RaiseLockQueryAsync(paper.Id, "wrong external mark", "Teacher One");
        Assert.True(qOk, qErr);

        // HOD approves with valid window
        var now = DateTime.UtcNow;
        var (aOk, aErr) = await svc.ReviewLockQueryAsync(paper.Id, approve: true, note: "ok", now.AddMinutes(-5), now.AddHours(2), "HOD");
        Assert.True(aOk, aErr);

        // Save inside window must succeed
        var (save3Ok, save3Err) = await svc.SaveMarkAsync(paper.Id, 1, 22m, 70m, "corrected", "T");
        Assert.True(save3Ok, save3Err);
        Assert.Equal(22m, (await db.ExamMarks.FirstAsync()).InternalMark);
    }

    [Fact]
    public async Task Rejected_query_keeps_marks_locked()
    {
        var (db, paper) = Setup("reject-flow");
        var user = new FakeUser { Faculty = await db.Faculty.FirstAsync() };
        var svc = new ExamService(db, user, new FakeSettings(db), new FakeAttendance());

        await svc.SaveMarkAsync(paper.Id, 1, 20m, 60m, null, "T");
        await svc.LockMarksAsync(paper.Id, "HOD");
        await svc.RaiseLockQueryAsync(paper.Id, "reason", "T");
        var (aOk, aErr) = await svc.ReviewLockQueryAsync(paper.Id, approve: false, note: "rejected", null, null, "HOD");
        Assert.True(aOk, aErr);

        var (saveOk, saveErr) = await svc.SaveMarkAsync(paper.Id, 1, 10m, 0m, null, "T");
        Assert.False(saveOk);
    }

    [Fact]
    public async Task Approval_requires_valid_window_start_before_end()
    {
        var (db, paper) = Setup("window-validation");
        var user = new FakeUser { Faculty = await db.Faculty.FirstAsync() };
        var svc = new ExamService(db, user, new FakeSettings(db), new FakeAttendance());

        await svc.SaveMarkAsync(paper.Id, 1, 20m, 60m, null, "T");
        await svc.LockMarksAsync(paper.Id, "HOD");
        await svc.RaiseLockQueryAsync(paper.Id, "reason", "T");
        var now = DateTime.UtcNow;
        var (aOk, aErr) = await svc.ReviewLockQueryAsync(paper.Id, approve: true, note: null, now, now.AddHours(-1), "HOD");
        Assert.False(aOk);
        Assert.Contains("window", aErr ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
