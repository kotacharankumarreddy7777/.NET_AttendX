namespace AttendX.Services;

public interface IClock
{
    DateTime Now { get; }
}

public class SystemClock : IClock
{
    private static readonly TimeZoneInfo IstZone = GetIstZone();
    public static TimeSpan IstOffset => new TimeSpan(5, 30, 0);
    public DateTime Now => new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero).ToOffset(IstOffset).LocalDateTime;
    public DateTime NowUtc => DateTime.UtcNow;
    public DateTimeOffset NowOffset => new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero).ToOffset(IstOffset);

    public static DateTimeOffset ToIst(DateTimeOffset utcDto)
    {
        var utc = utcDto.Offset == TimeSpan.Zero ? utcDto : utcDto.ToUniversalTime();
        return utc.ToOffset(IstOffset);
    }

    public static DateTime IstFromUtc(DateTime utc)
    {
        var dto = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return dto.ToOffset(IstOffset).LocalDateTime;
    }

    private static TimeZoneInfo GetIstZone()
    {
        foreach (var id in new[] { "India Standard Time", "Asia/Kolkata" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");
    }
}
