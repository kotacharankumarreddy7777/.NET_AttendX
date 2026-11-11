namespace AttendX.Services;

public interface ICalendarService
{
    Task<string?> GetNonWorkingReasonAsync(DateTime date, int? branchId = null, int? sectionId = null, int? semester = null);
    Task<List<Holiday>> UpcomingAsync(int take);
    Task<HashSet<DateTime>> HolidayDatesAsync(DateTime from, DateTime to, int? branchId = null, int? sectionId = null, int? semester = null);
}

public class CalendarService(AppDbContext db, IClock clock) : ICalendarService
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public async Task<string?> GetNonWorkingReasonAsync(DateTime date, int? branchId = null, int? sectionId = null, int? semester = null)
    {
        date = date.Date;
        if (date.DayOfWeek == DayOfWeek.Sunday) return "Sunday";
        var h = await _db.Holidays.AsNoTracking().FirstOrDefaultAsync(x => x.Date == date && x.Type == "Holiday");
        if (h != null) return h.Name;
        var q = _db.ExamTimetables.AsNoTracking().Where(x => x.ExamDate.Date == date && x.IsActive);
        if (branchId != null) q = q.Where(x => x.BranchId == branchId);
        if (sectionId != null) q = q.Where(x => x.SectionId == sectionId);
        if (semester != null) q = q.Where(x => x.Semester == semester);
        var e = await q.FirstOrDefaultAsync();
        return e != null ? "Exam in Progress" : null;
    }

    public async Task<List<Holiday>> UpcomingAsync(int take)
    {
        var today = _clock.NowUtc.Date;
        return await _db.Holidays.AsNoTracking().Where(h => h.Date >= today && h.Type == "Holiday").OrderBy(h => h.Date).Take(take).ToListAsync();
    }

    public async Task<HashSet<DateTime>> HolidayDatesAsync(DateTime from, DateTime to, int? branchId = null, int? sectionId = null, int? semester = null)
    {
        var f = from.Date; var t = to.Date;
        var list = await _db.Holidays.AsNoTracking().Where(h => h.Type == "Holiday" && h.Date >= f && h.Date <= t).Select(h => h.Date).ToListAsync();
        var q = _db.ExamTimetables.AsNoTracking().Where(e => e.IsActive && e.ExamDate.Date >= f && e.ExamDate.Date <= t);
        if (branchId != null) q = q.Where(x => x.BranchId == branchId);
        if (sectionId != null) q = q.Where(x => x.SectionId == sectionId);
        if (semester != null) q = q.Where(x => x.Semester == semester);
        var exams = await q.Select(e => e.ExamDate.Date).ToListAsync();
        return [.. list, .. exams];
    }
}
