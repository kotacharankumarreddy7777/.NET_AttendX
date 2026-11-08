namespace AttendX.Services;

public interface IReportService
{
    Task<ReportTable> BuildAsync(ReportFilter filter);
    Task<List<StudentSummary>> GetStudentSummariesAsync(ReportFilter filter);
}

/// <summary>Builds every report as a generic ReportTable. Authorization scope is applied here (HOD = own department, Faculty = own classes).</summary>
public class ReportService(AppDbContext db, ICurrentUser user, ISettingsService settings, IClock clock) : IReportService
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly ISettingsService _settings = settings;
    private readonly IClock _clock = clock;

    private async Task ApplyScopeAsync(ReportFilter f)
    {
        if (_user.IsAdmin) return;
        if (_user.IsHod) { f.BranchId = await _user.GetScopeBranchIdAsync(); return; }
        var me = await _user.GetFacultyAsync();
        f.FacultyId = me?.Id ?? -1;          // faculty only ever sees records of their own classes
    }

    private IQueryable<Attendance> Query(ReportFilter f)
    {
        IQueryable<Attendance> q = _db.Attendance.AsNoTracking();
        if (f.BranchId != null) q = q.Where(a => a.BranchId == f.BranchId);
        if (f.SectionId != null) q = q.Where(a => a.SectionId == f.SectionId);
        if (f.Semester != null) q = q.Where(a => a.Semester == f.Semester);
        if (f.SubjectId != null) q = q.Where(a => a.SubjectId == f.SubjectId);
        if (f.FacultyId != null) q = q.Where(a => a.FacultyId == f.FacultyId);
        if (f.From != null) { var from = f.From.Value.Date; q = q.Where(a => a.Date >= from); }
        if (f.To != null) { var to = f.To.Value.Date; q = q.Where(a => a.Date <= to); }
        return q;
    }

    public async Task<List<StudentSummary>> GetStudentSummariesAsync(ReportFilter f)
    {
        await ApplyScopeAsync(f);
        return await SummariesAsync(f);
    }

    private async Task<List<StudentSummary>> SummariesAsync(ReportFilter f)
    {
        var settings = await _settings.GetAsync();
        var rules = await _db.ThresholdRules.AsNoTracking().ToListAsync();
        var aggs = await Query(f).GroupBy(a => a.StudentId).Select(g => new
        {
            StudentId = g.Key, Total = g.Count(),
            Present = g.Count(x => x.Status == AttendanceStatus.Present),
            Late = g.Count(x => x.Status == AttendanceStatus.Late),
            Absent = g.Count(x => x.Status == AttendanceStatus.Absent)
        }).ToListAsync();
        var ids = aggs.Select(a => a.StudentId).ToList();
        var students = await _db.Students.AsNoTracking().Include(s => s.Branch).Include(s => s.Section)
            .Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);

        var list = new List<StudentSummary>();
        foreach (var a in aggs)
        {
            if (!students.TryGetValue(a.StudentId, out var s)) continue;
            var pct = AttendanceMath.Percent(a.Present, a.Late, a.Total, settings.LateWeight);
            var th = ThresholdResolver.Resolve(rules, s.BranchId, s.Semester, s.SectionId, settings.DefaultThreshold);
            list.Add(new StudentSummary
            {
                StudentId = s.Id, Code = s.StudentID, Name = s.FullName, Branch = s.Branch?.Code ?? s.BranchId.ToString(), Section = s.Section?.Name ?? "", Semester = s.Semester,
                BranchId = s.BranchId, SectionId = s.SectionId, Total = a.Total, Present = a.Present, Late = a.Late, Absent = a.Absent,
                Percent = pct, Threshold = th, Risk = AttendanceMath.Risk(pct, th),
                ClassesNeeded = AttendanceMath.ClassesNeeded(a.Present + a.Late * settings.LateWeight, a.Total, th)
            });
        }
        return list;
    }

    public async Task<ReportTable> BuildAsync(ReportFilter f)
    {
        await ApplyScopeAsync(f);
        var settings = await _settings.GetAsync();
        var w = settings.LateWeight;
        var today = _clock.Now.Date;
        var t = new ReportTable { Threshold = settings.DefaultThreshold };
        string range = f.From != null || f.To != null ? $"{f.From:dd MMM yyyy} to {f.To:dd MMM yyyy}".Replace(" to ", " to ") : "All dates";
        t.Subtitle = $"AttendX - {range} - generated {_clock.Now:dd MMM yyyy HH:mm}";
        string Pct(int p, int l, int tot) => AttendanceMath.Percent(p, l, tot, w).ToString("0.0") + "%";

        switch (f.Type)
        {
            case "student":
            {
                t.Title = "Student-wise attendance (by subject)";
                t.Columns = new() { "Student ID", "Name", "Section", "Subject", "Total", "Present", "Late", "Absent", "%" };
                var g = await Query(f).GroupBy(a => new { a.StudentId, a.SubjectId }).Select(x => new
                {
                    x.Key.StudentId, x.Key.SubjectId, Total = x.Count(),
                    P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late), A = x.Count(r => r.Status == AttendanceStatus.Absent)
                }).ToListAsync();
                var sids = g.Select(x => x.StudentId).Distinct().ToList();
                var subIds = g.Select(x => x.SubjectId).Distinct().ToList();
                var st = await _db.Students.AsNoTracking().Include(s => s.Section).Where(s => sids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
                var sub = await _db.Subjects.AsNoTracking().Where(s => subIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
                var totalForLog = g.Count;
                foreach (var r in g.Where(x => st.TryGetValue(x.StudentId, out _) && sub.TryGetValue(x.SubjectId, out _)).OrderBy(x => st[x.StudentId].StudentID).ThenBy(x => sub[x.SubjectId].SubjectCode).Take(3000))
                {
                    var s = st[r.StudentId]; var u = sub[r.SubjectId];
                    var sec = s.Section?.Name ?? "";
                    t.Rows.Add(new[] { s.StudentID, s.FullName, sec, u.SubjectCode + " " + u.SubjectName,
                        r.Total.ToString(), r.P.ToString(), r.L.ToString(), r.A.ToString(), Pct(r.P, r.L, r.Total) });
                }
                if (totalForLog > 3000) t.Rows.Add(new[] { $"Truncated: {totalForLog} rows total, showing 3000", "", "", "", "", "", "", "", "" });
                t.PercentColumn = 8;
                break;
            }
            case "subject":
            {
                t.Title = "Subject-wise attendance";
                t.Columns = new() { "Subject code", "Subject", "Records", "Present", "Late", "Absent", "%" };
                var g = await Query(f).GroupBy(a => a.SubjectId).Select(x => new { Id = x.Key, Total = x.Count(),
                    P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late), A = x.Count(r => r.Status == AttendanceStatus.Absent) }).ToListAsync();
                var ids = g.Select(x => x.Id).ToList();
                var sub = await _db.Subjects.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
                foreach (var r in g.OrderBy(x => sub[x.Id].SubjectCode))
                {
                    var s = sub[r.Id];
                    t.Rows.Add(new[] { s.SubjectCode, s.SubjectName, r.Total.ToString(), r.P.ToString(), r.L.ToString(), r.A.ToString(), Pct(r.P, r.L, r.Total) });
                }
                t.PercentColumn = 6; t.Footer = Footer(g.Sum(x => x.Total), g.Sum(x => x.P), g.Sum(x => x.L), g.Sum(x => x.A), w, 2);
                break;
            }
            case "section":
            {
                t.Title = "Section-wise attendance";
                t.Columns = new() { "Branch", "Section", "Records", "Present", "Late", "Absent", "%" };
                var g = await Query(f).GroupBy(a => a.SectionId).Select(x => new { Id = x.Key, Total = x.Count(),
                    P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late), A = x.Count(r => r.Status == AttendanceStatus.Absent) }).ToListAsync();
                var ids = g.Select(x => x.Id).ToList();
                var sec = await _db.Sections.AsNoTracking().Include(s => s.Branch).Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
                foreach (var r in g.OrderBy(x => sec.TryGetValue(x.Id, out var sec0) ? sec0.Branch?.Code ?? "" : "").ThenBy(x => sec.TryGetValue(x.Id, out var sec0b) ? sec0b.Name : ""))
                {
                    if (!sec.TryGetValue(r.Id, out var sd)) continue;
                    var bc = sd.Branch?.Code ?? "";
                    t.Rows.Add(new[] { bc, sd.Name, r.Total.ToString(), r.P.ToString(), r.L.ToString(), r.A.ToString(), Pct(r.P, r.L, r.Total) });
                }
                t.PercentColumn = 6; t.Footer = Footer(g.Sum(x => x.Total), g.Sum(x => x.P), g.Sum(x => x.L), g.Sum(x => x.A), w, 2);
                break;
            }
            case "faculty":
            {
                t.Title = "Faculty attendance activity";
                t.Columns = new() { "Faculty ID", "Faculty", "Sessions marked", "Records", "Last marked" };
                var g = await Query(f).GroupBy(a => a.FacultyId).Select(x => new { Id = x.Key, Sessions = x.Select(r => r.AttendanceSessionId).Distinct().Count(), Records = x.Count(), Last = x.Max(r => r.MarkedAt) }).ToListAsync();
                var ids = g.Select(x => x.Id).ToList();
                var fac = await _db.Faculty.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
                foreach (var r in g.OrderByDescending(x => x.Sessions))
                {
                    if (!fac.TryGetValue(r.Id, out var fv)) continue;
                    t.Rows.Add(new[] { fv.FacultyID, fv.FullName, r.Sessions.ToString(), r.Records.ToString(), r.Last.ToString("dd MMM yyyy HH:mm") });
                }
                break;
            }
            case "daily":
            {
                if (f.From == null && f.To == null) { f.From = today.AddDays(-7); f.To = today; }
                t.Title = "Daily attendance report";
                t.Columns = new() { "Date", "Day", "Sessions", "Records", "Present", "Late", "Absent", "%" };
                var g = await Query(f).GroupBy(a => a.Date).Select(x => new { D = x.Key, Sessions = x.Select(r => r.AttendanceSessionId).Distinct().Count(), Total = x.Count(),
                    P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late), A = x.Count(r => r.Status == AttendanceStatus.Absent) }).ToListAsync();
                foreach (var r in g.OrderBy(x => x.D))
                    t.Rows.Add(new[] { r.D.ToString("dd MMM yyyy"), r.D.DayOfWeek.ToString(), r.Sessions.ToString(), r.Total.ToString(), r.P.ToString(), r.L.ToString(), r.A.ToString(), Pct(r.P, r.L, r.Total) });
                t.PercentColumn = 7; t.Footer = Footer(g.Sum(x => x.Total), g.Sum(x => x.P), g.Sum(x => x.L), g.Sum(x => x.A), w, 3);
                t.Subtitle = $"AttendX - {f.From:dd MMM yyyy} to {f.To:dd MMM yyyy}";
                break;
            }
            case "monthly":
            {
                if (f.From == null) f.From = new DateTime(today.Year, today.Month, 1).AddMonths(-5);
                t.Title = "Monthly attendance report";
                t.Columns = new() { "Month", "Sessions", "Records", "Present", "Late", "Absent", "%" };
                var g = await Query(f).GroupBy(a => new { a.Date.Year, a.Date.Month }).Select(x => new { x.Key.Year, x.Key.Month, Sessions = x.Select(r => r.AttendanceSessionId).Distinct().Count(), Total = x.Count(),
                    P = x.Count(r => r.Status == AttendanceStatus.Present), L = x.Count(r => r.Status == AttendanceStatus.Late), A = x.Count(r => r.Status == AttendanceStatus.Absent) }).ToListAsync();
                foreach (var r in g.OrderBy(x => x.Year).ThenBy(x => x.Month))
                    t.Rows.Add(new[] { new DateTime(r.Year, r.Month, 1).ToString("MMMM yyyy"), r.Sessions.ToString(), r.Total.ToString(), r.P.ToString(), r.L.ToString(), r.A.ToString(), Pct(r.P, r.L, r.Total) });
                t.PercentColumn = 6; t.Footer = Footer(g.Sum(x => x.Total), g.Sum(x => x.P), g.Sum(x => x.L), g.Sum(x => x.A), w, 2);
                break;
            }
            case "range":
            case "low":
            case "defaulter":
            {
                if (f.Type == "range" && f.From == null && f.To == null) { f.From = new DateTime(today.Year, today.Month, 1); f.To = today; }
                var list = await SummariesAsync(f);
                if (f.Type == "low") { list = [.. list.Where(s => s.Percent < s.Threshold + 10)]; t.Title = "Low attendance report (below threshold + 10 points)"; }
                else if (f.Type == "defaulter") { list = [.. list.Where(s => s.Percent < s.Threshold)]; t.Title = "Defaulter report (below the required threshold)"; }
                else t.Title = "Date-range attendance report";
                t.Columns = new() { "Student ID", "Name", "Branch", "Section", "Sem", "Total", "Present", "Late", "Absent", "%", "Threshold", "Risk", "Classes needed" };
                var totalCount = list.Count;
                foreach (var s in list.OrderBy(x => x.Percent).ThenBy(x => x.Code).Take(3000))
                    t.Rows.Add(new[] { s.Code, s.Name, s.Branch ?? "", s.Section ?? "", s.Semester.ToString(), s.Total.ToString(), s.Present.ToString(), s.Late.ToString(), s.Absent.ToString(),
                        s.Percent.ToString("0.0") + "%", s.Threshold.ToString("0.#") + "%", s.Risk, s.ClassesNeeded == 0 ? "-" : s.ClassesNeeded < 0 ? "n/a" : s.ClassesNeeded.ToString() });
                if (totalCount > 3000) t.Rows.Add(new[] { $"Truncated: {totalCount} rows total, showing 3000", "", "", "", "", "", "", "", "", "", "", "", "" });
                t.PercentColumn = 9;
                break;
            }
            default:
                t.Title = "Unknown report";
                break;
        }
        return t;
    }

    private static string[] Footer(int total, int p, int l, int a, decimal w, int leadingBlank)
    {
        var cells = new List<string>();
        for (int i = 0; i < leadingBlank; i++) cells.Add(i == 0 ? "TOTAL" : "");
        cells.AddRange(new[] { total.ToString(), p.ToString(), l.ToString(), a.ToString(), AttendanceMath.Percent(p, l, total, w).ToString("0.0") + "%" });
        return [.. cells];
    }
}
