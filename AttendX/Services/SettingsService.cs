using Microsoft.Extensions.Options;

namespace AttendX.Services;

public interface ISettingsService
{
    Task<AttendanceSettings> GetAsync();
    Task SaveAsync(AttendanceSettings s);
    Task<AcademicYear> GetCurrentYearAsync();
}

public class SettingsService(AppDbContext db, IOptions<AttendXOptions> o) : ISettingsService
{
    private readonly AppDbContext _db = db;
    private readonly AttendXOptions _o = o.Value;

    public async Task<AttendanceSettings> GetAsync()
    {
        var s = await _db.AttendanceSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (s != null) return s;
        s = new AttendanceSettings
        {
            GraceMinutes = _o.GraceMinutes,
            DefaultThreshold = _o.DefaultThreshold,
            QrExpirySeconds = _o.QrExpirySeconds,
            LocationRequirement = Enum.TryParse<LocationRequirement>(_o.LocationRequirement, true, out var lr) ? lr : LocationRequirement.Optional,
            LateAfterMinutes = _o.LateAfterMinutes,
            OfflineSyncEnabled = _o.OfflineSyncEnabled,
            OfflineMaxDelayMinutes = _o.OfflineMaxDelayMinutes,
            PeriodCount = _o.PeriodCount,
            LateWeight = 1m
        };
        _db.AttendanceSettings.Add(s);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _db.ChangeTracker.Clear();
            var existing = await _db.AttendanceSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (existing != null) return existing;
            throw new InvalidOperationException("Could not create attendance settings. Please try again.", ex);
        }
        return s;
    }

    public async Task SaveAsync(AttendanceSettings s)
    {
        var row = await GetAsync();
        _db.AttendanceSettings.Attach(row);
        row.GraceMinutes = s.GraceMinutes;
        row.DefaultThreshold = s.DefaultThreshold;
        row.QrExpirySeconds = s.QrExpirySeconds;
        row.LocationRequirement = s.LocationRequirement;
        row.LateWeight = s.LateWeight;
        row.LateAfterMinutes = s.LateAfterMinutes;
        row.OfflineSyncEnabled = s.OfflineSyncEnabled;
        row.OfflineMaxDelayMinutes = s.OfflineMaxDelayMinutes;
        row.PeriodCount = s.PeriodCount;
        row.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<AcademicYear> GetCurrentYearAsync()
    {
        var y = await _db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(a => a.IsCurrent)
                ?? await _db.AcademicYears.AsNoTracking().OrderByDescending(a => a.StartDate).FirstOrDefaultAsync();
        if (y != null) return y;
        int start;
        try
        {
            start = int.TryParse((_o.AcademicYear ?? "").Split('-')[0], out var sy) ? sy : DateTime.Today.Year;
        }
        catch
        {
            start = DateTime.Today.Year;
        }
        var ny = new AcademicYear { Name = _o.AcademicYear, StartDate = new DateTime(start, 6, 1), EndDate = new DateTime(start + 1, 5, 31), IsCurrent = true };
        _db.AcademicYears.Add(ny);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException ex) { throw new InvalidOperationException("Could not create academic year.", ex); }
        return ny;
    }
}
