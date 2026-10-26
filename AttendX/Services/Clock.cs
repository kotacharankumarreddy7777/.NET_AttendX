namespace AttendX.Services;

/// <summary>Server clock abstraction. SERVER time is authoritative for every attendance rule (never the browser clock).</summary>
public interface IClock { DateTime Now { get; } }

public class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}
