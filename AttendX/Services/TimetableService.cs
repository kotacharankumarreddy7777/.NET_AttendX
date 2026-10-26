using System.Text.Json;

namespace AttendX.Services;

public interface ITimetableService
{
    Task<List<string>> ValidateAsync(TimetableFormViewModel m);
    Task<(bool Ok, List<string> Errors)> SaveAsync(TimetableFormViewModel m);
    Task<bool> DeactivateAsync(int id);
    Task<TimetableGridViewModel> BuildGridAsync(int? branchId, int? sectionId, int? semester, int? facultyId, int? scopeBranchId);
}

public class TimetableService(AppDbContext db, ISettingsService settings, IAuditService audit) : ITimetableService
{
    private readonly AppDbContext _db = db;
    private readonly ISettingsService _settings = settings;
    private readonly IAuditService _audit = audit;

    /// <summary>All timetable rules. Returns a list of clear, human readable problems (empty list = valid).</summary>
    public async Task<List<string>> ValidateAsync(TimetableFormViewModel m)
    {
        var errors = new List<string>();
        var year = await _settings.GetCurrentYearAsync();
        var settings = await _settings.GetAsync();

        var section = await _db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == m.SectionId);
        var subject = await _db.Subjects.AsNoTracking().FirstOrDefaultAsync(s => s.Id == m.SubjectId);
        var faculty = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(f => f.Id == m.FacultyId);

        if (section == null || !section.IsActive) errors.Add("Selected section is invalid or inactive.");
        else if (section.BranchId != m.BranchId) errors.Add("The selected section does not belong to the selected branch.");

        if (subject == null || !subject.IsActive) errors.Add("Selected subject is invalid or inactive.");
        else
        {
            if (subject.BranchId != m.BranchId) errors.Add($"Subject {subject.SubjectCode} belongs to a different branch.");
            if (subject.Semester != m.Semester) errors.Add($"Subject {subject.SubjectCode} is taught in semester {subject.Semester}, not semester {m.Semester}.");
        }

        if (faculty == null || !faculty.IsActive) errors.Add("Selected faculty is invalid or inactive.");
        if (m.Day == DayOfWeek.Sunday) errors.Add("Timetable runs Monday to Saturday only.");
        if (m.Period < 1 || m.Period > settings.PeriodCount) errors.Add($"Period must be between 1 and {settings.PeriodCount}.");

        var pd = await _db.PeriodDefinitions.AsNoTracking().FirstOrDefaultAsync(p => p.PeriodNo == m.Period);
        if (pd == null) errors.Add($"Period {m.Period} has no timing defined. Set it in Settings > Periods.");
        else if (pd.StartTime >= pd.EndTime) errors.Add($"Period {m.Period} has an invalid time range.");

        if (errors.Count > 0) return errors;

        int ignore = m.Id ?? 0;
        var sectionClash = await _db.Timetables.AsNoTracking().Include(t => t.Subject).Include(t => t.Faculty)
            .FirstOrDefaultAsync(t => t.IsActive && t.AcademicYearId == year.Id && t.SectionId == m.SectionId && t.Day == m.Day && t.Period == m.Period && t.Id != ignore);
        if (sectionClash != null)
            errors.Add($"Section {section!.Name} already has {sectionClash.Subject!.SubjectCode} ({sectionClash.Faculty!.FullName}) in period {m.Period} on {m.Day}.");

        var facultyClash = await _db.Timetables.AsNoTracking().Include(t => t.Section).Include(t => t.Subject)
            .FirstOrDefaultAsync(t => t.IsActive && t.AcademicYearId == year.Id && t.FacultyId == m.FacultyId && t.Day == m.Day && t.Period == m.Period && t.Id != ignore);
        if (facultyClash != null)
            errors.Add($"{faculty!.FullName} is already teaching {facultyClash.Subject!.SubjectCode} to section {facultyClash.Section!.Name} in period {m.Period} on {m.Day} (double booking).");

        if (!m.AutoMap)
        {
            bool mapped = await _db.FacultySubjectMappings.AnyAsync(x => x.Active && x.FacultyId == m.FacultyId && x.SubjectId == m.SubjectId && x.SectionId == m.SectionId && x.AcademicYearId == year.Id);
            if (!mapped) errors.Add($"{faculty!.FullName} is not assigned to teach {subject!.SubjectCode} for section {section!.Name}. Add the Faculty-Subject mapping first (or tick the auto-assign option).");
        }
        return errors;
    }

    public async Task<(bool Ok, List<string> Errors)> SaveAsync(TimetableFormViewModel m)
    {
        var errors = await ValidateAsync(m);
        if (errors.Count > 0) return (false, errors);

        var year = await _settings.GetCurrentYearAsync();
        var pd = await _db.PeriodDefinitions.AsNoTracking().FirstAsync(p => p.PeriodNo == m.Period);

        Timetable t;
        string? old = null;
        if (m.Id.HasValue)
        {
            t = await _db.Timetables.FirstAsync(x => x.Id == m.Id.Value);
            old = Snapshot(t);
        }
        else
        {
            t = new Timetable { AcademicYearId = year.Id };
            _db.Timetables.Add(t);
        }
        t.BranchId = m.BranchId; t.SectionId = m.SectionId; t.Semester = m.Semester; t.Day = m.Day; t.Period = m.Period;
        t.SubjectId = m.SubjectId; t.FacultyId = m.FacultyId; t.RoomNo = m.RoomNo.Trim(); t.ClassType = m.ClassType;
        t.StartTime = pd.StartTime; t.EndTime = pd.EndTime; t.IsActive = true;

        if (m.AutoMap)
        {
            var map = await _db.FacultySubjectMappings.FirstOrDefaultAsync(x => x.FacultyId == m.FacultyId && x.SubjectId == m.SubjectId && x.SectionId == m.SectionId && x.AcademicYearId == year.Id);
            if (map == null)
                _db.FacultySubjectMappings.Add(new FacultySubjectMapping { FacultyId = m.FacultyId, SubjectId = m.SubjectId, BranchId = m.BranchId, SectionId = m.SectionId, Semester = m.Semester, AcademicYearId = year.Id, Active = true });
            else map.Active = true;
        }

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { return (false, new List<string> { "Someone else just booked this slot. Reload the timetable and try again." }); }

        await _audit.LogAsync(m.Id.HasValue ? "TimetableUpdated" : "TimetableCreated", "Timetable", t.Id.ToString(), old, Snapshot(t), branchId: t.BranchId);
        return (true, new List<string>());
    }

    public async Task<bool> DeactivateAsync(int id)
    {
        var t = await _db.Timetables.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return false;
        t.IsActive = false;   // history stays: attendance sessions keep pointing to this row
        await _db.SaveChangesAsync();
        await _audit.LogAsync("TimetableRemoved", "Timetable", id.ToString(), Snapshot(t), null, branchId: t.BranchId);
        return true;
    }

    public async Task<TimetableGridViewModel> BuildGridAsync(int? branchId, int? sectionId, int? semester, int? facultyId, int? scopeBranchId)
    {
        var vm = new TimetableGridViewModel { BranchId = branchId, SectionId = sectionId, Semester = semester, FacultyId = facultyId };
        vm.Periods = await _db.PeriodDefinitions.AsNoTracking().OrderBy(p => p.PeriodNo).ToListAsync();
        if (!vm.HasFilter) return vm;

        var year = await _settings.GetCurrentYearAsync();
        var q = _db.Timetables.AsNoTracking().Include(t => t.Subject).Include(t => t.Faculty).Include(t => t.Section)
            .Where(t => t.IsActive && t.AcademicYearId == year.Id);
        if (scopeBranchId != null) q = q.Where(t => t.BranchId == scopeBranchId);
        if (branchId != null) q = q.Where(t => t.BranchId == branchId);
        if (sectionId != null) q = q.Where(t => t.SectionId == sectionId);
        if (semester != null) q = q.Where(t => t.Semester == semester);
        if (facultyId != null) q = q.Where(t => t.FacultyId == facultyId);

        var rows = await q.OrderBy(t => t.Section!.Name).ToListAsync();
        foreach (var g in rows.GroupBy(t => $"{(int)t.Day}-{t.Period}")) vm.Cells[g.Key] = [.. g];
        return vm;
    }

    private static string Snapshot(Timetable t) => JsonSerializer.Serialize(new { t.BranchId, t.SectionId, t.Semester, Day = t.Day.ToString(), t.Period, t.SubjectId, t.FacultyId, t.RoomNo, t.ClassType, t.IsActive });
}
