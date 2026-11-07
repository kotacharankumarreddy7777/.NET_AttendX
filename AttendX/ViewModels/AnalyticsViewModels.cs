namespace AttendX.ViewModels;

public class StudentRisk
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Section { get; set; } = "";
    public int Total { get; set; }
    public decimal Percent { get; set; }
    public decimal Threshold { get; set; }
    public decimal RecentPercent { get; set; }
    public decimal PreviousPercent { get; set; }
    public decimal SlopePerWeek { get; set; }
    public decimal Projected { get; set; }
    public int ClassesNeeded { get; set; }
    public string Risk { get; set; } = "";
    public string Trend { get; set; } = "";
    public string Recommendation { get; set; } = "";
}

public class SubjectRisk
{
    public string Subject { get; set; } = "";
    public decimal Percent { get; set; }
    public decimal Threshold { get; set; }
    public int Records { get; set; }
    public string Note { get; set; } = "";
}

public class SectionAnomaly
{
    public string Section { get; set; } = "";
    public decimal Recent { get; set; }
    public decimal Previous { get; set; }
    public decimal Average { get; set; }
    public decimal ZScore { get; set; }
    public string Note { get; set; } = "";
}

public class AnalyticsViewModel
{
    public List<StudentRisk> Students { get; set; } = new();
    public List<SubjectRisk> Subjects { get; set; } = new();
    public List<SectionAnomaly> Sections { get; set; } = new();
    public ChartSeries Weekly { get; set; } = new();
    public int AtRiskCount { get; set; }
    public int DecliningCount { get; set; }
}
