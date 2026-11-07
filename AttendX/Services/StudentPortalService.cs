using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Services;

public interface IStudentPortalService
{
    Task<StudentDashboardViewModel?> BuildAsync();
    Task<(bool Ok, string Message)> RaiseQueryAsync(DateTime date, int subjectId, string message);
    Task<ReportTable?> BuildReportAsync();
}

/// <summary>Everything the student dashboard shows. The student is ALWAYS resolved from the logged-in identity - never from an id in the URL.</summary>
public class StudentPortalService(AppDbContext db, ICurrentUser user, ISettingsService settings, ICalendarService calendar, INoticeService notices, IAuditService audit, IClock clock) : IStudentPortalService
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly ISettingsService _settings = settings;
    private readonly ICalendarService _calendar = calendar;
    private readonly INoticeService _notices = notices;
    private readonly IAuditService _audit = audit;
    private readonly IClock _clock = clock;

    private async Task<Student?> MeAsync()
    {
        var uid = _user.UserId;
        if (uid == null) return null;
        return await _db.Students.AsNoTracking().Include(s => s.Branch).Include(s => s.Section).FirstOrDefaultAsync(s => s.IdentityUserId == uid && s.IsActive);
    }

    public async Task<StudentDashboardViewModel?> BuildAsync()
    {
        var me = await MeAsync();
        if (me == null) return null;
        var settings = await _settings.GetAsync();
        var year = await _settings.GetCurrentYearAsync();
        var rules = await _db.ThresholdRules.AsNoTracking().ToListAsync();
        var w = settings.LateWeight;
        var now = _clock.Now; var today = now.Date;

        var vm = new StudentDashboardViewModel { Student = me, BranchName = me.Branch!.Name, SectionName = me.Section!.Name };
        vm.ClassStrength = await _db.Students.CountAsync(s => s.IsActive && s.BranchId == me.BranchId && s.SectionId == me.SectionId && s.Semester == me.Semester);
        vm.Threshold = ThresholdResolver.Resolve(rules, me.BranchId, me.Semester, me.SectionId, settings.DefaultThreshold);

        // ----- timetable of my class
        var tt = await _db.Timetables.AsNoTracking().Include(t => t.Subject).Include(t => t.Faculty)
            .Where(t => t.IsActive && t.AcademicYearId == year.Id && t.BranchId == me.BranchId && t.SectionId == me.SectionId && t.Semester == me.Semester).ToListAsync();
        vm.Periods = await _db.PeriodDefinitions.AsNoTracking().OrderBy(p => p.PeriodNo).ToListAsync();
        foreach (var t in tt)
            vm.Week[$"{(int)t.Day}-{t.Period}"] = new TimetableCellVm { SubjectCode = t.Subject!.SubjectCode, SubjectName = t.Subject.SubjectName, Faculty = t.Faculty!.FullName, Room = t.RoomNo };
        var facultyBySubject = tt.GroupBy(t => t.SubjectId).ToDictionary(g => g.Key, g => g.First().Faculty!);

        // ----- attendance of this student
        var rows = await _db.Attendance.AsNoTracking().Where(a => a.StudentId == me.Id).Select(a => new { a.Date, a.Period, a.SubjectId, a.Status }).ToListAsync();
        vm.Total = rows.Count;
        vm.Present = rows.Count(r => r.Status == AttendanceStatus.Present);
        vm.Late = rows.Count(r => r.Status == AttendanceStatus.Late);
        vm.Absent = rows.Count(r => r.Status == AttendanceStatus.Absent);
        vm.Percent = AttendanceMath.Percent(vm.Present, vm.Late, vm.Total, w);
        vm.Risk = AttendanceMath.Risk(vm.Percent, vm.Threshold);
        decimal eff = vm.Present + vm.Late * w;
        vm.ClassesNeeded = AttendanceMath.ClassesNeeded(eff, vm.Total, vm.Threshold);

        var recent = rows.Where(r => r.Date >= today.AddDays(-13)).ToList();
        decimal recentRatio = recent.Count == 0 ? (vm.Total == 0 ? 0 : eff / vm.Total)
            : recent.Sum(r => r.Status == AttendanceStatus.Present ? 1m : r.Status == AttendanceStatus.Late ? w : 0m) / recent.Count;
        decimal future = 4 * (recent.Count / 2m);
        vm.Projected = vm.Total + future == 0 ? 0 : Math.Round((eff + recentRatio * future) * 100m / (vm.Total + future), 1);

        if (vm.Total == 0) vm.Advice = "No attendance has been recorded for you yet. It will appear here after your first class.";
        else if (vm.Risk == "Safe") vm.Advice = $"Great work! You are comfortably above the {vm.Threshold}% requirement. Keep it up.";
        else if (vm.Risk == "Watch") vm.Advice = $"You are just above the {vm.Threshold}% requirement. A few absences would put you below it - try not to miss classes.";
        else vm.Advice = vm.ClassesNeeded > 0
            ? $"You are below the {vm.Threshold}% requirement. Attend the next {vm.ClassesNeeded} classes in a row to get back to {vm.Threshold}%."
            : $"You are below the {vm.Threshold}% requirement.";

        // ----- subject-wise
        var subjectIds = rows.Select(r => r.SubjectId).Distinct().ToList();
        var subjects = await _db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        foreach (var g in rows.GroupBy(r => r.SubjectId))
        {
            int p = g.Count(r => r.Status == AttendanceStatus.Present), l = g.Count(r => r.Status == AttendanceStatus.Late), a = g.Count(r => r.Status == AttendanceStatus.Absent);
            vm.Subjects.Add(new StudentSubjectRow
            {
                Code = subjects[g.Key].SubjectCode, Name = subjects[g.Key].SubjectName,
                FacultyName = facultyBySubject.TryGetValue(g.Key, out var fc) ? fc.FullName : "",
                Total = g.Count(), Present = p, Late = l, Absent = a, Percent = AttendanceMath.Percent(p, l, g.Count(), w)
            });
        }
        vm.Subjects = [.. vm.Subjects.OrderBy(s => s.Percent)];

        // ----- my faculty
        vm.Faculty = [.. tt.GroupBy(t => t.SubjectId).Select(g => g.First())
            .Select(t => new FacultyContact { Name = t.Faculty!.FullName, Email = t.Faculty.Email, Subject = t.Subject!.SubjectCode + " " + t.Subject.SubjectName }).OrderBy(f => f.Subject)];
        vm.SubjectOptions = [.. tt.GroupBy(t => t.SubjectId).Select(g => g.First()).OrderBy(t => t.Subject!.SubjectCode).Select(t => new SelectListItem(t.Subject!.SubjectCode + " - " + t.Subject.SubjectName, t.SubjectId.ToString()))];

        // ----- heat map (last 35 days)
        var holidays = await _calendar.HolidayDatesAsync(today.AddDays(-34), today);
        var byDay = rows.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.ToList());
        for (int i = 34; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            var cell = new HeatDay { Date = d };
            if (d.DayOfWeek == DayOfWeek.Sunday) { cell.Css = "s"; cell.Tip = $"{d:dd MMM} - Sunday"; }
            else if (holidays.Contains(d)) { cell.Css = "h"; cell.Tip = $"{d:dd MMM} - Holiday"; }
            else if (byDay.TryGetValue(d, out var list))
            {
                int pr = list.Count(x => x.Status == AttendanceStatus.Present), ab = list.Count(x => x.Status == AttendanceStatus.Absent), la = list.Count(x => x.Status == AttendanceStatus.Late);
                cell.Css = ab == 0 ? (la > 0 ? "l" : "p") : (ab >= pr + la ? "a" : "l");
                cell.Tip = $"{d:dd MMM} - {pr} present, {la} late, {ab} absent";
            }
            else cell.Tip = $"{d:dd MMM} - no class recorded";
            vm.Heat.Add(cell);
        }

        // ----- today
        vm.NonWorkingReason = await _calendar.GetNonWorkingReasonAsync(today);
        if (vm.NonWorkingReason == null)
        {
            var todayStatus = rows.Where(r => r.Date == today).ToDictionary(r => r.Period, r => r.Status.ToString());
            foreach (var t in tt.Where(x => x.Day == today.DayOfWeek).OrderBy(x => x.Period))
            {
                var state = AttendanceWindow.State(now, today, t.StartTime, t.EndTime, settings.GraceMinutes);
                vm.TodayClasses.Add(new SlotView
                {
                    TimetableId = t.Id, Period = t.Period, Start = t.StartTime, End = t.EndTime, SubjectCode = t.Subject!.SubjectCode, SubjectName = t.Subject.SubjectName,
                    FacultyName = t.Faculty!.FullName, Room = t.RoomNo, SectionName = me.Section.Name, BranchCode = me.Branch.Code, State = state,
                    MyStatus = todayStatus.TryGetValue(t.Period, out var st) ? st : null
                });
            }
        }
        var next = FindNextClass(tt, now);
        vm.NextClassText = next;

        // ----- misc
        vm.Recent = await _db.Attendance.AsNoTracking().Include(a => a.Subject).Where(a => a.StudentId == me.Id).OrderByDescending(a => a.Date).ThenByDescending(a => a.Period).Take(25).ToListAsync();
        vm.Notices = await _notices.VisibleAsync(6);
        vm.Holidays = await _calendar.UpcomingAsync(5);
        vm.Queries = await _db.AttendanceQueries.AsNoTracking().Include(q => q.Subject).Where(q => q.StudentId == me.Id).OrderByDescending(q => q.CreatedAt).Take(10).ToListAsync();
        var uid = _user.UserId;
        vm.Logins = await _db.AuditLogs.AsNoTracking().Where(a => a.ActorId == uid && a.Action == "Login").OrderByDescending(a => a.Timestamp).Take(5).ToListAsync();
        vm.LastLogin = vm.Logins.Skip(1).FirstOrDefault()?.Timestamp ?? vm.Logins.FirstOrDefault()?.Timestamp;
        return vm;
    }

    private string? FindNextClass(List<Timetable> tt, DateTime now)
    {
        for (int add = 0; add < 8; add++)
        {
            var d = now.Date.AddDays(add);
            if (d.DayOfWeek == DayOfWeek.Sunday) continue;
            var next = tt.Where(t => t.Day == d.DayOfWeek && (add > 0 || d + t.StartTime > now)).OrderBy(t => t.Period).FirstOrDefault();
            if (next != null)
                return (add == 0 ? "Today" : add == 1 ? "Tomorrow" : d.ToString("dddd")) + $" {next.StartTime.Hours:00}:{next.StartTime.Minutes:00} - {next.Subject!.SubjectName}";
        }
        return null;
    }

    public async Task<(bool Ok, string Message)> RaiseQueryAsync(DateTime date, int subjectId, string message)
    {
        var me = await MeAsync();
        if (me == null) return (false, "Student not found.");
        message = (message ?? "").Trim();
        if (message.Length < 5 || message.Length > 500) return (false, "Please describe the problem in 5 to 500 characters.");
        date = date.Date;
        var today = _clock.Now.Date;
        if (date > today || date < today.AddDays(-30)) return (false, "You can raise a query only for the last 30 days.");

        var year = await _settings.GetCurrentYearAsync();
        var slot = await _db.Timetables.AsNoTracking().FirstOrDefaultAsync(t => t.IsActive && t.AcademicYearId == year.Id && t.BranchId == me.BranchId && t.SectionId == me.SectionId && t.Semester == me.Semester && t.SubjectId == subjectId);
        if (slot == null) return (false, "That subject is not part of your timetable.");
        if (await _db.AttendanceQueries.AnyAsync(q => q.StudentId == me.Id && q.SubjectId == subjectId && q.RecordDate == date && q.Status == "Open"))
            return (false, "You already have an open query for this subject and date.");

        _db.AttendanceQueries.Add(new AttendanceQuery { StudentId = me.Id, BranchId = me.BranchId, SubjectId = subjectId, FacultyId = slot.FacultyId, RecordDate = date, Message = message });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("QueryRaised", "AttendanceQuery", me.StudentID, newValues: message, branchId: me.BranchId);
        return (true, "Your query was sent to the faculty. You will see the answer here.");
    }

    public async Task<ReportTable?> BuildReportAsync()
    {
        var vm = await BuildAsync();
        if (vm == null) return null;
        var t = new ReportTable
        {
            Title = $"Attendance report - {vm.Student.FullName} ({vm.Student.StudentID})",
            Subtitle = $"{vm.BranchName} / {vm.SectionName} / Semester {vm.Student.Semester} - overall {vm.Percent}% (required {vm.Threshold}%) - generated {_clock.Now:dd MMM yyyy HH:mm}",
            Columns = new() { "Subject code", "Subject", "Faculty", "Total", "Present", "Late", "Absent", "%" },
            PercentColumn = 7, Threshold = vm.Threshold
        };
        foreach (var s in vm.Subjects)
            t.Rows.Add(new[] { s.Code, s.Name, s.FacultyName, s.Total.ToString(), s.Present.ToString(), s.Late.ToString(), s.Absent.ToString(), s.Percent.ToString("0.0") + "%" });
        t.Footer = new[] { "OVERALL", "", "", vm.Total.ToString(), vm.Present.ToString(), vm.Late.ToString(), vm.Absent.ToString(), vm.Percent.ToString("0.0") + "%" };
        return t;
    }
}
