namespace AttendX.Services;

public interface ICalendarService
{
    /// <summary>null = working day. Otherwise "Sunday" or the holiday name.</summary>
    Task<string?> GetNonWorkingReasonAsync(DateTime date);
    Task<List<Holiday>> UpcomingAsync(int take);
    Task<HashSet<DateTime>> HolidayDatesAsync(DateTime from, DateTime to);
}

public class CalendarService(AppDbContext db, IClock clock) : ICalendarService
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public async Task<string?> GetNonWorkingReasonAsync(DateTime date)
    {
        date = date.Date;
        if (date.DayOfWeek == DayOfWeek.Sunday) return "Sunday";
        var h = await _db.Holidays.AsNoTracking().FirstOrDefaultAsync(x => x.Date == date && x.Type == "Holiday");
        return h?.Name;
    }

    public async Task<List<Holiday>> UpcomingAsync(int take)
    {
        var today = _clock.Now.Date;
        return await _db.Holidays.AsNoTracking().Where(h => h.Date >= today).OrderBy(h => h.Date).Take(take).ToListAsync();
    }

    public async Task<HashSet<DateTime>> HolidayDatesAsync(DateTime from, DateTime to)
    {
        var f = from.Date; var t = to.Date;
        var list = await _db.Holidays.AsNoTracking().Where(h => h.Type == "Holiday" && h.Date >= f && h.Date <= t).Select(h => h.Date).ToListAsync();
        return [.. list];
    }
}
