namespace AttendX.Infrastructure;

/// <summary>Tiny presentation helpers used by Razor views.</summary>
public static class UiHelpers
{
    public static string PctClass(decimal pct, decimal threshold)
        => pct >= threshold + 10 ? "pct-ok" : pct >= threshold ? "pct-warn" : "pct-bad";

    public static string RiskBadge(string risk) => risk switch
    {
        "Safe" => "bg-success", "Watch" => "bg-warning", "At Risk" => "bg-orange", _ => "bg-danger"
    };

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var p = name.Replace("Dr. ", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length == 0 ? "?" : (p[0][0].ToString() + (p.Length > 1 ? p[^1][0].ToString() : "")).ToUpperInvariant();
    }

    public static string Time(TimeSpan t) => new DateTime(2000, 1, 1, t.Hours % 24, t.Minutes, 0).ToString("h:mm tt");
    public static string DateTimeShort(DateTime dt) => dt.ToString("dd MMM hh:mm tt");
    public static string DateTimeFull(DateTime dt) => dt.ToString("dd MMM yyyy hh:mm:ss tt");
    public static string DateTimeShortSec(DateTime dt) => dt.ToString("dd MMM yy hh:mm:ss tt");
}
