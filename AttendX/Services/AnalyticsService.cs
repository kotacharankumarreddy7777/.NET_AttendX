namespace AttendX.Services;

public interface IAnalyticsService
{
    Task<AnalyticsViewModel> BuildAsync();
}

/// <summary>
/// "Academic Attendance Analytics" - works fully offline, no paid AI API.
/// Techniques: weighted attendance %, 2-week vs previous-2-week comparison, least-squares trend (slope per week),
/// 4-week projection, "classes needed" arithmetic and z-score anomaly detection for sections.
/// Every result carries a plain-English explanation.
/// </summary>
public class AnalyticsService(AppDbContext db, ICurrentUser user, ISettingsService settings, IClock clock) : IAnalyticsService
{
    private record Row(int StudentId, int SectionId, int SubjectId, DateTime Date, AttendanceStatus Status);

    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly ISettingsService _settings = settings;
    private readonly IClock _clock = clock;

    private static decimal Pct(IEnumerable<Row> rows, decimal w)
    {
        int tot = 0; decimal eff = 0;
        foreach (var r in rows) { tot++; eff += r.Status == AttendanceStatus.Present ? 1 : r.Status == AttendanceStatus.Late ? w : 0; }
        return tot == 0 ? 0 : Math.Round(eff * 100m / tot, 1);
    }

    /// <summary>Least squares slope of y over x = 0..n-1 (percentage points per week).</summary>
    public static decimal Slope(IList<decimal> y)
    {
        int n = y.Count;
        if (n < 2) return 0;
        decimal mx = (n - 1) / 2m, my = y.Average(), num = 0, den = 0;
        for (int i = 0; i < n; i++) { num += (i - mx) * (y[i] - my); den += (i - mx) * (i - mx); }
        return den == 0 ? 0 : Math.Round(num / den, 2);
    }

    public async Task<AnalyticsViewModel> BuildAsync()
    {
        var settings = await _settings.GetAsync();
        var rules = await _db.ThresholdRules.AsNoTracking().ToListAsync();
        var w = settings.LateWeight;
        var today = _clock.Now.Date;
        var from = today.AddDays(-55);

        IQueryable<Attendance> q = _db.Attendance.AsNoTracking().Where(a => a.Date >= from && a.Date <= today);
        if (!_user.IsAdmin)
        {
            if (_user.IsHod) { var sb = await _user.GetScopeBranchIdAsync(); q = q.Where(a => a.BranchId == sb); }
            else { var me = await _user.GetFacultyAsync(); var fid = me?.Id ?? -1; q = q.Where(a => a.FacultyId == fid); }
        }
        var rows = (await q.Select(a => new { a.StudentId, a.SectionId, a.SubjectId, a.Date, a.Status }).ToListAsync())
            .Select(a => new Row(a.StudentId, a.SectionId, a.SubjectId, a.Date, a.Status)).ToList();

        var vm = new AnalyticsViewModel();
        if (rows.Count == 0) return vm;

        // ----- weekly overall trend chart (oldest -> newest)
        for (int wk = 7; wk >= 0; wk--)
        {
            var end = today.AddDays(-7 * wk); var start = end.AddDays(-6);
            var part = rows.Where(r => r.Date >= start && r.Date <= end).ToList();
            vm.Weekly.Labels.Add(wk == 0 ? "This week" : $"{wk}w ago");
            vm.Weekly.Values.Add(part.Count == 0 ? 0 : Pct(part, w));
        }

        // ----- students
        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var students = await _db.Students.AsNoTracking().Include(s => s.Section).Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var recentStart = today.AddDays(-13); var prevStart = today.AddDays(-27);

        foreach (var g in rows.GroupBy(r => r.StudentId))
        {
            if (!students.TryGetValue(g.Key, out var s)) continue;
            var all = g.ToList();
            var th = ThresholdResolver.Resolve(rules, s.BranchId, s.Semester, s.SectionId, settings.DefaultThreshold);
            var overall = Pct(all, w);
            var recent = all.Where(r => r.Date >= recentStart).ToList();
            var prev = all.Where(r => r.Date >= prevStart && r.Date < recentStart).ToList();
            var recentPct = recent.Count == 0 ? overall : Pct(recent, w);
            var prevPct = prev.Count == 0 ? overall : Pct(prev, w);

            var weekly = new List<decimal>();
            for (int wk = 7; wk >= 0; wk--)
            {
                var end = today.AddDays(-7 * wk); var start = end.AddDays(-6);
                var part = all.Where(r => r.Date >= start && r.Date <= end).ToList();
                if (part.Count > 0) weekly.Add(Pct(part, w));
            }
            var slope = Slope(weekly);

            decimal eff = all.Sum(r => r.Status == AttendanceStatus.Present ? 1m : r.Status == AttendanceStatus.Late ? w : 0m);
            int total = all.Count;
            decimal perWeek = weekly.Count == 0 ? 0 : (decimal)total / weekly.Count;
            decimal future = 4 * perWeek;
            decimal projected = total + future == 0 ? overall : Math.Round((eff + recentPct / 100m * future) * 100m / (total + future), 1);
            int needed = AttendanceMath.ClassesNeeded(eff, total, th);
            var risk = AttendanceMath.Risk(overall, th);
            var trend = slope <= -3m || recentPct - prevPct <= -8m ? "Declining" : slope >= 3m || recentPct - prevPct >= 8m ? "Improving" : "Stable";

            var rec = new List<string> { AttendanceMath.RiskExplanation(overall, th) };
            if (needed > 0) rec.Add($"Needs {needed} consecutive Present classes to reach {th}%.");
            if (needed < 0) rec.Add("The threshold cannot be reached mathematically.");
            if (trend == "Declining") rec.Add($"Recent 2-week attendance ({recentPct}%) is lower than the 2 weeks before ({prevPct}%) - arrange a mentor conversation.");
            if (projected < th) rec.Add($"If the recent pattern continues, attendance will be about {projected}% after 4 weeks (below {th}%).");
            if (risk == "Safe" && trend != "Declining") rec.Add("No action needed.");

            if (risk != "Safe" || trend == "Declining")
                vm.Students.Add(new StudentRisk
                {
                    Code = s.StudentID, Name = s.FullName, Section = s.Section!.Name, Total = total, Percent = overall, Threshold = th,
                    RecentPercent = recentPct, PreviousPercent = prevPct, SlopePerWeek = slope, Projected = projected, ClassesNeeded = needed,
                    Risk = risk, Trend = trend, Recommendation = string.Join(" ", rec)
                });
        }
        vm.AtRiskCount = vm.Students.Count(x => x.Risk is "At Risk" or "Critical");
        vm.DecliningCount = vm.Students.Count(x => x.Trend == "Declining");
        vm.Students = [.. vm.Students.OrderBy(x => x.Percent).Take(40)];

        // ----- subjects
        var subjIds = rows.Select(r => r.SubjectId).Distinct().ToList();
        var subjects = await _db.Subjects.AsNoTracking().Where(s => subjIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        foreach (var g in rows.GroupBy(r => r.SubjectId))
        {
            var p = Pct(g, w);
            vm.Subjects.Add(new SubjectRisk
            {
                Subject = subjects[g.Key].SubjectCode + " " + subjects[g.Key].SubjectName, Percent = p, Threshold = settings.DefaultThreshold, Records = g.Count(),
                Note = p < settings.DefaultThreshold ? "Below threshold - check timetable slot, teaching quality or student interest." : p < settings.DefaultThreshold + 10 ? "Watch" : "Healthy"
            });
        }
        vm.Subjects = [.. vm.Subjects.OrderBy(x => x.Percent).Take(10)];

        // ----- section anomalies (z-score against all sections in scope)
        var secIds = rows.Select(r => r.SectionId).Distinct().ToList();
        var secs = await _db.Sections.AsNoTracking().Include(s => s.Branch).Where(s => secIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var perSection = rows.GroupBy(r => r.SectionId).Select(g => new
        {
            Id = g.Key,
            Recent = Pct(g.Where(r => r.Date >= recentStart), w),
            Prev = Pct(g.Where(r => r.Date >= prevStart && r.Date < recentStart), w)
        }).ToList();
        if (perSection.Count > 0)
        {
            var mean = perSection.Average(x => x.Recent);
            var std = (decimal)Math.Sqrt(perSection.Average(x => (double)((x.Recent - mean) * (x.Recent - mean))));
            foreach (var x in perSection)
            {
                var z = std == 0 ? 0 : Math.Round((x.Recent - mean) / std, 2);
                bool flag = (perSection.Count >= 3 && z <= -1m) || x.Prev - x.Recent >= 10m;
                vm.Sections.Add(new SectionAnomaly
                {
                    Section = secs[x.Id].Branch!.Code + " / " + secs[x.Id].Name, Recent = x.Recent, Previous = x.Prev, Average = Math.Round(mean, 1), ZScore = z,
                    Note = flag ? (x.Prev - x.Recent >= 10m ? $"Dropped {x.Prev - x.Recent:0.#} points in two weeks." : $"{Math.Abs(z)} standard deviations below the average of all sections.") : "Normal"
                });
            }
            vm.Sections = [.. vm.Sections.OrderBy(x => x.ZScore).Take(12)];
        }
        return vm;
    }
}
