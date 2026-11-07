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
        // First run: build the settings row from appsettings.json
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
        await _db.SaveChangesAsync();
        return s;
    }

    public async Task SaveAsync(AttendanceSettings s)
    {
        var row = await _db.AttendanceSettings.FirstAsync();
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
        var start = int.TryParse(_o.AcademicYear.Split('-')[0], out var sy) ? sy : DateTime.Today.Year;
        var ny = new AcademicYear { Name = _o.AcademicYear, StartDate = new DateTime(start, 6, 1), EndDate = new DateTime(start + 1, 5, 31), IsCurrent = true };
        _db.AcademicYears.Add(ny);
        await _db.SaveChangesAsync();
        return ny;
    }
}
