namespace AttendX.Services;

/// <summary>Semester -> Year rule. 1-2 => 1, 3-4 => 2, 5-6 => 3, 7-8 => 4.</summary>
public static class YearHelper
{
    public static int FromSemester(int semester) => semester is >= 1 and <= 8 ? (semester + 1) / 2 : 0;
}

/// <summary>Pure attendance calculations (no database) so they are easy to unit test.</summary>
public static class AttendanceMath
{
    /// <summary>Attendance % = (Present + Late x LateWeight) / Total x 100, rounded to 1 decimal.</summary>
    public static decimal Percent(int present, int late, int total, decimal lateWeight = 1m)
        => total <= 0 ? 0m : Math.Round((present + late * lateWeight) * 100m / total, 1);

    /// <summary>Explainable risk level compared with the applicable threshold.</summary>
    public static string Risk(decimal percent, decimal threshold)
    {
        if (percent >= threshold + 10) return "Safe";
        if (percent >= threshold) return "Watch";
        if (percent >= threshold - 10) return "At Risk";
        return "Critical";
    }

    public static string RiskExplanation(decimal percent, decimal threshold)
    {
        var level = Risk(percent, threshold);
        return level switch
        {
            "Safe" => $"{percent}% is at least 10 points above the {threshold}% threshold.",
            "Watch" => $"{percent}% is within 10 points above the {threshold}% threshold - a few absences would put the student below it.",
            "At Risk" => $"{percent}% is below the {threshold}% threshold by up to 10 points.",
            _ => $"{percent}% is more than 10 points below the {threshold}% threshold."
        };
    }

    /// <summary>How many consecutive Present classes are needed to reach the threshold. 0 = already there, -1 = impossible (threshold 100).</summary>
    public static int ClassesNeeded(decimal effectivePresent, int total, decimal threshold)
    {
        if (total <= 0) return 0;
        if (effectivePresent * 100m / total >= threshold) return 0;
        if (threshold >= 100m) return -1;
        var x = (threshold * total - 100m * effectivePresent) / (100m - threshold);
        return (int)Math.Ceiling(x);
    }
}

public static class ThresholdResolver
{
    /// <summary>Most specific matching rule wins (section > semester > branch). Falls back to the default threshold.</summary>
    public static decimal Resolve(IEnumerable<ThresholdRule> rules, int branchId, int semester, int sectionId, decimal defaultThreshold)
    {
        var ordered = rules.OrderBy(r => r.Id).ToList();
        ThresholdRule? best = null;
        int bestScore = -1;
        foreach (var r in ordered)
        {
            if (r.BranchId != null && r.BranchId != branchId) continue;
            if (r.Semester != null && r.Semester != semester) continue;
            if (r.SectionId != null && r.SectionId != sectionId) continue;
            int score = (r.SectionId != null ? 4 : 0) + (r.Semester != null ? 2 : 0) + (r.BranchId != null ? 1 : 0);
            if (score > bestScore) { best = r; bestScore = score; }
        }
        return best?.Percent ?? defaultThreshold;
    }
}

/// <summary>The time rule: attendance is allowed only on the same calendar day, from (start - grace) to (end + grace).</summary>
public static class AttendanceWindow
{
    public static (DateTime Start, DateTime End) For(DateTime date, TimeSpan start, TimeSpan end, int graceMinutes)
        => (date.Date + start - TimeSpan.FromMinutes(graceMinutes), date.Date + end + TimeSpan.FromMinutes(graceMinutes));

    public static bool IsOpen(DateTime now, DateTime date, TimeSpan start, TimeSpan end, int graceMinutes)
    {
        if (now.Date != date.Date) return false;
        var (s, e) = For(date, start, end, graceMinutes);
        if (e < s) return now >= s || now <= e;
        return now >= s && now <= e;
    }

    /// <summary>Upcoming / Open / Closed for a slot on the given date.</summary>
    public static string State(DateTime now, DateTime date, TimeSpan start, TimeSpan end, int graceMinutes)
    {
        var (s, e) = For(date, start, end, graceMinutes);
        if (now < s) return "Upcoming";
        if (now > e) return "Closed";
        return "Open";
    }
}
