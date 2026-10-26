namespace AttendX.ViewModels;

public class ChartSeries
{
    public List<string> Labels { get; set; } = new();
    public List<decimal> Values { get; set; } = new();
}

public class DashboardCharts
{
    public ChartSeries Group { get; set; } = new();       // by branch (admin) / by section (HOD)
    public ChartSeries Semester { get; set; } = new();
    public ChartSeries Daily { get; set; } = new();
    public ChartSeries Monthly { get; set; } = new();
    public ChartSeries Subject { get; set; } = new();
    public string GroupTitle { get; set; } = "Branch-wise attendance %";
}

public class AdminDashboardViewModel
{
    public string ScopeName { get; set; } = "All branches";
    public bool IsHod { get; set; }
    public int TotalStudents { get; set; }
    public int ActiveStudents { get; set; }
    public int ActiveFaculty { get; set; }
    public int Hods { get; set; }
    public int Branches { get; set; }
    public int Sections { get; set; }
    public int Subjects { get; set; }
    public int TodayClasses { get; set; }
    public int TodaySessions { get; set; }
    public int TodayRecords { get; set; }
    public decimal AvgAttendance { get; set; }
    public int LowAttendanceCount { get; set; }
    public List<StudentSummary> LowStudents { get; set; } = new();
    public List<SlotView> MissingSlots { get; set; } = new();
    public List<string> Anomalies { get; set; } = new();
    public List<FacultyActivityRow> FacultyActivity { get; set; } = new();
    public DashboardCharts Charts { get; set; } = new();
}

public class FacultyActivityRow
{
    public string Name { get; set; } = "";
    public int Sessions { get; set; }
    public DateTime? LastMarked { get; set; }
}

public class FacultyDashboardViewModel
{
    public Faculty Faculty { get; set; } = null!;
    public string Department { get; set; } = "";
    public List<SlotView> Today { get; set; } = new();
    public SlotView? Current { get; set; }
    public SlotView? Next { get; set; }
    public List<string> Assigned { get; set; } = new();
    public int TodayPresent { get; set; }
    public int TodayAbsent { get; set; }
    public int TodayLate { get; set; }
    public FacultyHomeExtras Extra { get; set; } = new();
}
