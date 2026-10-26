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

    public static string Initials(string name)
    {
        var p = name.Replace("Dr. ", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length == 0 ? "?" : (p[0][0].ToString() + (p.Length > 1 ? p[^1][0].ToString() : "")).ToUpperInvariant();
    }

    public static string Time(TimeSpan t) => $"{t.Hours:00}:{t.Minutes:00}";
}
