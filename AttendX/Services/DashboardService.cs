namespace AttendX.Services;

public interface IDashboardService
{
    Task<AdminDashboardViewModel> BuildAdminOrHodAsync();
    Task<FacultyDashboardViewModel?> BuildFacultyAsync();
}

public class DashboardService(AppDbContext db, ICurrentUser user, ISettingsService settingsService, IAttendanceService attendance, IReportService reports,
    IAnalyticsService analytics, IClock clock, ICalendarService calendar, INoticeService notices) : IDashboardService
{
    public async Task<AdminDashboardViewModel> BuildAdminOrHodAsync()
    {
        var scope = await user.GetScopeBranchIdAsync();            // null = admin
        var settings = await settingsService.GetAsync();
        var year = await settingsService.GetCurrentYearAsync();
        var w = settings.LateWeight;
        var now = clock.Now; var today = now.Date;
        var vm = new AdminDashboardViewModel { IsHod = scope != null };

        var students = db.Students.AsNoTracking().AsQueryable();
        var faculty = db.Faculty.AsNoTracking().Where(f => f.IsActive);
        var sections = db.Sections.AsNoTracking().Where(s => s.IsActive);
        var subjects = db.Subjects.AsNoTracking().Where(s => s.IsActive);
        var tt = db.Timetables.AsNoTracking().Where(t => t.IsActive && t.AcademicYearId == year.Id && t.Day == today.DayOfWeek);
        IQueryable<Attendance> att = db.Attendance.AsNoTracking();

        if (scope != null)
        {
            var b = await db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scope);
            vm.ScopeName = b?.Name ?? "Department";
            students = students.Where(s => s.BranchId == scope);
            faculty = faculty.Where(f => f.BranchId == scope);
            sections = sections.Where(s => s.BranchId == scope);
            subjects = subjects.Where(s => s.BranchId == scope);
            tt = tt.Where(t => t.BranchId == scope);
            att = att.Where(a => a.BranchId == scope);
        }

        vm.TotalStudents = await students.CountAsync();
        vm.ActiveStudents = await students.CountAsync(s => s.IsActive);
        vm.ActiveFaculty = await faculty.CountAsync();
        vm.Hods = await faculty.CountAsync(f => f.IsHOD);
        vm.Branches = scope != null ? 1 : await db.Branches.CountAsync(b => b.IsActive);
        vm.Sections = await sections.CountAsync();
        vm.Subjects = await subjects.CountAsync();
        vm.TodayClasses = await tt.CountAsync();
        vm.TodaySessions = await db.AttendanceSessions.AsNoTracking().Where(s => s.Date == today && tt.Select(t => t.Id).Contains(s.TimetableId)).CountAsync();
        vm.TodayRecords = await att.CountAsync(a => a.Date == today);

        // average attendance over the last 30 days (one grouped scan instead of three counts)
        var from30 = today.AddDays(-29);
        var last30 = att.Where(a => a.Date >= from30 && a.Date <= today);
        var byStatus = await last30.GroupBy(a => a.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        var tot = byStatus.Sum(x => x.C);
        var pres = byStatus.FirstOrDefault(x => x.Key == AttendanceStatus.Present)?.C ?? 0;
        var late = byStatus.FirstOrDefault(x => x.Key == AttendanceStatus.Late)?.C ?? 0;
        vm.AvgAttendance = AttendanceMath.Percent(pres, late, tot, w);

        // low attendance students (overall threshold rules)
        var summaries = await reports.GetStudentSummariesAsync(new ReportFilter { Type = "low" });
        var below = summaries.Where(s => s.Percent < s.Threshold).OrderBy(s => s.Percent).ToList();
        vm.LowAttendanceCount = below.Count;
        vm.LowStudents = [.. below.Take(8)];

        // today's classes not marked yet (all states: Upcoming/Open/Closed, not just Closed)
        var slots = await attendance.GetSlotsAsync(today, null, scope, null);
        vm.MissingSlots = [.. slots.Where(s => !s.HasSession).OrderBy(s => s.Period).ThenBy(s => s.BranchCode)];

        // faculty activity (last 7 days): sessions are counted straight from AttendanceSessions instead of
        // COUNT(DISTINCT AttendanceSessionId) over every attendance row.
        var from7 = today.AddDays(-6);
        var scopeFacultyIds = faculty.Select(f => f.Id);
        var sessions7 = await db.AttendanceSessions.AsNoTracking()
            .Where(s => s.Date >= from7 && s.Date <= today && scopeFacultyIds.Contains(s.FacultyId))
            .GroupBy(s => s.FacultyId)
            .Select(g => new { g.Key, S = g.Count() })
            .ToListAsync();
        var lastMarked = await att.Where(a => a.Date >= from7)
            .GroupBy(a => a.FacultyId)
            .Select(g => new { g.Key, L = g.Max(x => x.MarkedAt) })
            .ToDictionaryAsync(x => x.Key, x => x.L);
        var fnames = await db.Faculty.AsNoTracking().Where(f => scopeFacultyIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.FullName);
        vm.FacultyActivity = [.. sessions7.OrderByDescending(a => a.S).Take(8)
            .Select(a => new FacultyActivityRow { Name = fnames.GetValueOrDefault(a.Key, "?"), Sessions = a.S, LastMarked = lastMarked.GetValueOrDefault(a.Key) })];

        var an = await analytics.BuildAsync();
        vm.Anomalies = [.. an.Sections.Where(s => s.Note != "Normal").Take(4).Select(s => $"{s.Section}: {s.Note}")];

        // ----- charts (all values come from the database)
        var ch = vm.Charts;
        if (scope == null)
        {
            ch.GroupTitle = "Branch-wise attendance % (30 days)";
            var g = await last30.GroupBy(a => a.BranchId).Select(x => new { x.Key, T = x.Count(), P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late) }).ToListAsync();
            var names = await db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code);
            foreach (var r in g.OrderBy(q => names[q.Key])) { ch.Group.Labels.Add(names[r.Key]); ch.Group.Values.Add(AttendanceMath.Percent(r.P, r.L, r.T, w)); }
        }
        else
        {
            ch.GroupTitle = "Section-wise attendance % (30 days)";
            var g = await last30.GroupBy(a => a.SectionId).Select(x => new { x.Key, T = x.Count(), P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late) }).ToListAsync();
            var attendanceMap = g.ToDictionary(x => x.Key, x => AttendanceMath.Percent(x.P, x.L, x.T, w));
            var allSections = await db.Sections.AsNoTracking().Where(s => s.BranchId == scope && s.IsActive).OrderBy(s => s.Name).ToListAsync();
            foreach (var sec in allSections)
            {
                ch.Group.Labels.Add(sec.Name);
                ch.Group.Values.Add(attendanceMap.GetValueOrDefault(sec.Id, 0m));
            }
        }
        var sg = await last30.GroupBy(a => a.Semester).Select(x => new { x.Key, T = x.Count(), P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late) }).ToListAsync();
        foreach (var r in sg.OrderBy(q => q.Key)) { ch.Semester.Labels.Add("Sem " + r.Key); ch.Semester.Values.Add(AttendanceMath.Percent(r.P, r.L, r.T, w)); }

        var from14 = today.AddDays(-13);
        var dg = await att.Where(a => a.Date >= from14 && a.Date <= today).GroupBy(a => a.Date).Select(x => new { x.Key, T = x.Count(), P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late) }).ToListAsync();
        foreach (var r in dg.OrderBy(q => q.Key)) { ch.Daily.Labels.Add(r.Key.ToString("dd MMM")); ch.Daily.Values.Add(AttendanceMath.Percent(r.P, r.L, r.T, w)); }

        var from6m = new DateTime(today.Year, today.Month, 1).AddMonths(-5);
        var mg = await att.Where(a => a.Date >= from6m).GroupBy(a => new { a.Date.Year, a.Date.Month }).Select(x => new { x.Key.Year, x.Key.Month, T = x.Count(), P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late) }).ToListAsync();
        foreach (var r in mg.OrderBy(q => q.Year).ThenBy(q => q.Month)) { ch.Monthly.Labels.Add(new DateTime(r.Year, r.Month, 1).ToString("MMM yyyy")); ch.Monthly.Values.Add(AttendanceMath.Percent(r.P, r.L, r.T, w)); }

        var sbj = await last30.GroupBy(a => a.SubjectId).Select(x => new { x.Key, T = x.Count(), P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late) }).ToListAsync();
        var sids = sbj.Select(x => x.Key).ToList();
        var snames = await db.Subjects.AsNoTracking().Where(s => sids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.SubjectCode);
        foreach (var r in sbj.Select(q => new { Name = snames[q.Key], Pct = AttendanceMath.Percent(q.P, q.L, q.T, w) }).OrderBy(q => q.Pct).Take(8))
        { ch.Subject.Labels.Add(r.Name); ch.Subject.Values.Add(r.Pct); }
        return vm;
    }

    public async Task<FacultyDashboardViewModel?> BuildFacultyAsync()
    {
        var me = await user.GetFacultyAsync();
        if (me == null) return null;
        var year = await settingsService.GetCurrentYearAsync();
        var today = clock.Now.Date;
        var vm = new FacultyDashboardViewModel { Faculty = me };
        var branch = me.BranchId == null ? null : await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == me.BranchId);
        vm.Department = branch?.Name ?? me.Department;
        vm.Today = await attendance.GetSlotsAsync(today, me.Id, null, null);
        vm.Current = vm.Today.FirstOrDefault(s => s.State == "Open");
        vm.Next = vm.Today.FirstOrDefault(s => s.State == "Upcoming");
        vm.Assigned = [.. (await db.FacultySubjectMappings.AsNoTracking().Include(m => m.Subject).Include(m => m.Section)
            .Where(m => m.FacultyId == me.Id && m.Active && m.AcademicYearId == year.Id).ToListAsync())
            .Select(m => $"{m.Subject!.SubjectCode} {m.Subject.SubjectName} - section {m.Section!.Name}").OrderBy(x => x)];
        var t = db.Attendance.AsNoTracking().Where(a => a.FacultyId == me.Id && a.Date == today);
        vm.TodayPresent = await t.CountAsync(a => a.Status == AttendanceStatus.Present);
        vm.TodayAbsent = await t.CountAsync(a => a.Status == AttendanceStatus.Absent);
        vm.TodayLate = await t.CountAsync(a => a.Status == AttendanceStatus.Late);
        await FillFacultyExtrasAsync(vm, me, year.Id, today);
        return vm;
    }

    private async Task FillFacultyExtrasAsync(FacultyDashboardViewModel vm, Faculty me, int yearId, DateTime today)
    {
        var ex = vm.Extra;
        var settings = await settingsService.GetAsync();
        ex.NonWorkingReason = await calendar.GetNonWorkingReasonAsync(today);
        ex.Periods = await db.PeriodDefinitions.AsNoTracking().OrderBy(p => p.PeriodNo).ToListAsync();

        var myTt = await db.Timetables.AsNoTracking().Include(x => x.Subject).Include(x => x.Section)
            .Where(x => x.IsActive && x.AcademicYearId == yearId && x.FacultyId == me.Id).ToListAsync();
        ex.ClassesThisWeek = myTt.Count;
        foreach (var x in myTt)
            ex.Week[$"{(int)x.Day}-{x.Period}"] = new TimetableCellVm { SubjectCode = x.Subject!.SubjectCode, SubjectName = x.Subject.SubjectName, Faculty = $"{x.Semester}{x.Section!.Name}", Room = x.RoomNo };

        var branchIds = myTt.Select(x => x.BranchId).Distinct().ToList();
        var strengths = await db.Students.AsNoTracking().Where(s => s.IsActive && branchIds.Contains(s.BranchId))
            .GroupBy(s => new { s.BranchId, s.SectionId, s.Semester }).Select(g => new { g.Key.BranchId, g.Key.SectionId, g.Key.Semester, C = g.Count() }).ToListAsync();
        var classKeys = myTt.Select(x => new { x.BranchId, x.SectionId, x.Semester }).Distinct().ToList();
        ex.StudentsTaught = classKeys.Sum(k => strengths.FirstOrDefault(s => s.BranchId == k.BranchId && s.SectionId == k.SectionId && s.Semester == k.Semester)?.C ?? 0);

        var from30 = today.AddDays(-29);
        var agg = await db.Attendance.AsNoTracking().Where(a => a.FacultyId == me.Id && a.Date >= from30 && a.Date <= today)
            .GroupBy(a => new { a.SubjectId, a.SectionId, a.Semester })
            .Select(g => new { g.Key.SubjectId, g.Key.SectionId, g.Key.Semester, T = g.Count(), P = g.Count(r => r.Status == AttendanceStatus.Present), L = g.Count(r => r.Status == AttendanceStatus.Late) }).ToListAsync();
        ex.AvgAttendance = AttendanceMath.Percent(agg.Sum(a => a.P), agg.Sum(a => a.L), agg.Sum(a => a.T), settings.LateWeight);
        foreach (var g in myTt.GroupBy(x => new { x.SubjectId, x.SectionId, x.Semester }).OrderBy(k => k.Key.Semester).ThenBy(k => k.First().Section!.Name))
        {
            var first = g.First();
            var a = agg.FirstOrDefault(z => z.SubjectId == g.Key.SubjectId && z.SectionId == g.Key.SectionId && z.Semester == g.Key.Semester);
            var st = strengths.FirstOrDefault(s => s.BranchId == first.BranchId && s.SectionId == g.Key.SectionId && s.Semester == g.Key.Semester);
            ex.Chips.Add(new FacultyClassChip { Label = $"{g.Key.Semester}{first.Section!.Name}-{first.Subject!.SubjectCode}", Strength = st?.C ?? 0, AvgPercent = a == null ? 0 : AttendanceMath.Percent(a.P, a.L, a.T, settings.LateWeight) });
        }

        var low = await reports.GetStudentSummariesAsync(new ReportFilter { Type = "low" });
        ex.LowStudents = [.. low.Where(s => s.Percent < s.Threshold).OrderBy(s => s.Percent).Take(8)];
        ex.Missing = [.. vm.Today.Where(s => s.State == "Closed" && !s.HasSession)];
        ex.Notices = await notices.VisibleAsync(5);
        ex.Holidays = await calendar.UpcomingAsync(4);
        ex.OpenQueries = await db.AttendanceQueries.CountAsync(q => q.FacultyId == me.Id && q.Status == "Open");
        var uid = user.UserId;
        var logins = await db.AuditLogs.AsNoTracking().Where(a => a.ActorId == uid && a.Action == "Login").OrderByDescending(a => a.Timestamp).Take(2).ToListAsync();
        ex.LastLogin = logins.Skip(1).FirstOrDefault()?.Timestamp ?? logins.FirstOrDefault()?.Timestamp;
    }
}
