namespace AttendX.Services;

public interface IClock
{
    /// <summary>Current instant as UTC (Kind=Utc). Authoritative time for persistence.</summary>
    DateTime NowUtc { get; }

    /// <summary>Current IST wall-clock (Kind=Unspecified). UI and period-window math only.</summary>
    DateTime NowIst { get; }

    /// <summary>The IST calendar date for the current instant, stored as a UTC-midnight instant (Kind=Utc).</summary>
    DateTime TodayIst { get; }

    /// <summary>UTC-midnight instant for the given IST calendar date (accepts any Kind; uses its wall-clock y/M/d).</summary>
    static DateTime DateUtc(DateTime any) => DateTime.SpecifyKind(new DateTime(any.Year, any.Month, any.Day), DateTimeKind.Utc);
}

public class SystemClock : IClock
{
    private static readonly TimeZoneInfo IstZone = GetIstZone();

    public static DateTimeOffset NowOffset() => new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero).ToOffset(IstOffset);

    public DateTime NowUtc => DateTime.UtcNow;
    public DateTime NowIst => NowOffset().LocalDateTime;
    public DateTime TodayIst => IClock.DateUtc(IstFromUtc(DateTime.UtcNow));

    public static DateTimeOffset ToIst(DateTimeOffset utcDto)
    {
        var utc = utcDto.Offset == TimeSpan.Zero ? utcDto : utcDto.ToUniversalTime();
        return utc.ToOffset(IstOffset);
    }

    public static DateTime IstFromUtc(DateTime utc)
    {
        var dto = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return new DateTimeOffset(dto).ToOffset(IstOffset).LocalDateTime;
    }

    public static DateTime UtcFromIst(DateTime unspecifiedIst)
        => new DateTimeOffset(DateTime.SpecifyKind(unspecifiedIst, DateTimeKind.Unspecified), IstOffset).UtcDateTime;

    private static TimeZoneInfo GetIstZone()
    {
        foreach (var id in new[] { "India Standard Time", "Asia/Kolkata" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");
    }

    public static TimeSpan IstOffset => new TimeSpan(5, 30, 0);
}
