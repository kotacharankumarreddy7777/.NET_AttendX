namespace AttendX.Services;

/// <summary>Server clock abstraction. SERVER time is authoritative for every attendance rule (never the browser clock). App runs live IST (Indian Standard Time, UTC+5:30).</summary>
public interface IClock { DateTime Now { get; } }

public class SystemClock : IClock
{
    private static readonly TimeZoneInfo IstZone = GetIstZone();
    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IstZone);

    private static TimeZoneInfo GetIstZone()
    {
        foreach (var id in new[] { "India Standard Time", "Asia/Kolkata" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        }
        // Fallback: UTC+5:30 custom
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");
    }
}
