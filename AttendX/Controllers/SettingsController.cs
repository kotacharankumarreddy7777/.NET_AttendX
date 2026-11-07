using Microsoft.AspNetCore.Mvc.Rendering;

namespace AttendX.Controllers;

[Authorize(Policy = PolicyNames.AdminOnly)]
public class SettingsController(AppDbContext db, ISettingsService settings, IAuditService audit) : Controller
{
    private readonly AppDbContext _db = db;
    private readonly ISettingsService _settings = settings;
    private readonly IAuditService _audit = audit;

    public async Task<IActionResult> Index()
    {
        var vm = new SettingsViewModel
        {
            Settings = await _settings.GetAsync(),
            Rules = await _db.ThresholdRules.AsNoTracking().Include(r => r.Branch).Include(r => r.Section).OrderBy(r => r.Id).ToListAsync(),
            Periods = await _db.PeriodDefinitions.AsNoTracking().OrderBy(p => p.PeriodNo).ToListAsync(),
            Years = await _db.AcademicYears.AsNoTracking().OrderByDescending(y => y.StartDate).ToListAsync(),
            Branches = await _db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToListAsync()
        };
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> SaveSettings([Bind("GraceMinutes,DefaultThreshold,QrExpirySeconds,LocationRequirement,LateWeight,LateAfterMinutes,OfflineSyncEnabled,OfflineMaxDelayMinutes,PeriodCount")] AttendanceSettings s)
    {
        if (s.DefaultThreshold < 1 || s.DefaultThreshold > 100 || s.LateWeight < 0 || s.LateWeight > 1 || s.GraceMinutes < 0 || s.GraceMinutes > 120 || s.QrExpirySeconds < 10 || s.QrExpirySeconds > 300 || s.PeriodCount < 1 || s.PeriodCount > 12)
        { TempData["Error"] = "A value is out of range (threshold 1-100, late weight 0-1, grace 0-120, QR expiry 10-300 s, periods 1-12)."; return RedirectToAction(nameof(Index)); }
        var old = await _settings.GetAsync();
        await _settings.SaveAsync(s);
        await _audit.LogAsync("SettingsChanged", "Settings", "1", $"grace={old.GraceMinutes}, threshold={old.DefaultThreshold}, qrExpiry={old.QrExpirySeconds}, location={old.LocationRequirement}",
            $"grace={s.GraceMinutes}, threshold={s.DefaultThreshold}, qrExpiry={s.QrExpirySeconds}, location={s.LocationRequirement}");
        TempData["Success"] = "Settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SavePeriods(int[] periodNo, string[] start, string[] end)
    {
        var rows = new List<(int No, TimeSpan S, TimeSpan E)>();
        for (int i = 0; i < periodNo.Length; i++)
        {
            if (!TimeSpan.TryParse(start[i], out var s) || !TimeSpan.TryParse(end[i], out var e) || s >= e)
            { TempData["Error"] = $"Period {periodNo[i]}: end time must be after start time."; return RedirectToAction(nameof(Index)); }
            rows.Add((periodNo[i], s, e));
        }
        var ordered = rows.OrderBy(r => r.No).ToList();
        for (int i = 1; i < ordered.Count; i++)
            if (ordered[i].S < ordered[i - 1].E) { TempData["Error"] = $"Period {ordered[i].No} overlaps period {ordered[i - 1].No}."; return RedirectToAction(nameof(Index)); }

        var existing = await _db.PeriodDefinitions.ToListAsync();
        foreach (var r in rows)
        {
            var p = existing.FirstOrDefault(x => x.PeriodNo == r.No);
            if (p == null) _db.PeriodDefinitions.Add(new PeriodDefinition { PeriodNo = r.No, StartTime = r.S, EndTime = r.E });
            else { p.StartTime = r.S; p.EndTime = r.E; }
            // keep timetable slots in sync with the new timing
            foreach (var slot in await _db.Timetables.Where(x => x.Period == r.No).ToListAsync()) { slot.StartTime = r.S; slot.EndTime = r.E; }
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync("PeriodsChanged", "Settings", null, details: string.Join("; ", rows.Select(r => $"P{r.No} {r.S.ToString(@"hh\:mm")}-{r.E.ToString(@"hh\:mm")}")));
        TempData["Success"] = "Period timings saved and timetable updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> AddRule(int? branchId, int? semester, int? sectionId, decimal percent, string? note)
    {
        if (percent < 1 || percent > 100) { TempData["Error"] = "Threshold must be 1-100."; return RedirectToAction(nameof(Index)); }
        if (sectionId != null && branchId != null && !await _db.Sections.AnyAsync(s => s.Id == sectionId && s.BranchId == branchId)) { TempData["Error"] = "Section does not belong to the branch."; return RedirectToAction(nameof(Index)); }
        _db.ThresholdRules.Add(new ThresholdRule { BranchId = branchId, Semester = semester, SectionId = sectionId, Percent = percent, Note = note });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ThresholdRuleAdded", "Settings", null, details: $"branch={branchId}, sem={semester}, section={sectionId}, {percent}%");
        TempData["Success"] = "Threshold rule added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteRule(int id)
    {
        var r = await _db.ThresholdRules.FindAsync(id);
        if (r != null) { _db.ThresholdRules.Remove(r); await _db.SaveChangesAsync(); await _audit.LogAsync("ThresholdRuleDeleted", "Settings", id.ToString()); }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> AddYear(string name, DateTime start, DateTime end)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 9 || end <= start) { TempData["Error"] = "Enter a name like 2027-28 and a valid date range."; return RedirectToAction(nameof(Index)); }
        if (await _db.AcademicYears.AnyAsync(y => y.Name == name.Trim())) { TempData["Error"] = "That academic year already exists."; return RedirectToAction(nameof(Index)); }
        _db.AcademicYears.Add(new AcademicYear { Name = name.Trim(), StartDate = start.Date, EndDate = end.Date });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("AcademicYearAdded", "Settings", name);
        TempData["Success"] = "Academic year added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SetCurrentYear(int id)
    {
        var years = await _db.AcademicYears.ToListAsync();
        foreach (var y in years) y.IsCurrent = y.Id == id;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CurrentYearChanged", "Settings", id.ToString());
        TempData["Success"] = "Current academic year changed. Timetables are per year - build the timetable for the new year.";
        return RedirectToAction(nameof(Index));
    }
}
