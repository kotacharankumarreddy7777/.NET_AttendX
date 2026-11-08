namespace AttendX.Infrastructure;

/// <summary>Role names used with ASP.NET Core Identity. Students have their own login.</summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Hod = "HOD";
    public const string Faculty = "Faculty";
    public const string Student = "Student";
    public static readonly string[] All = { Admin, Hod, Faculty, Student };
}

public static class PolicyNames
{
    public const string AdminOnly = "AdminOnly";
    public const string AdminOrHod = "AdminOrHod";
    public const string AnyStaff = "AnyStaff";
    public const string StudentOnly = "StudentOnly";
}

/// <summary>All tunable values live in appsettings.json ("AttendX" section). Admin can later change most of them in the Settings page (stored in DB).</summary>
public class AttendXOptions
{
    public string AcademicYear { get; set; } = "2026-27";
    public int GraceMinutes { get; set; } = 10;
    public decimal DefaultThreshold { get; set; } = 75;
    public int QrExpirySeconds { get; set; } = 30;
    public string LocationRequirement { get; set; } = "Optional";
    public bool OfflineSyncEnabled { get; set; } = true;
    public int OfflineMaxDelayMinutes { get; set; } = 120;
    public int LateAfterMinutes { get; set; } = 15;
    public int MaxUploadMB { get; set; } = 2;
    public int PeriodCount { get; set; } = 7;
    public string QrSecret { get; set; } = "";
    public string BackupFolder { get; set; } = "";
    public string AdminEmail { get; set; } = "admin@attendx.edu.in";
    public string AdminPassword { get; set; } = "";
    public string SeedFacultyPassword { get; set; } = "";
    public string SeedStudentPassword { get; set; } = "";
    public bool ForceAdminPasswordChange { get; set; } = false;
    public bool AutoExportExcel { get; set; } = true;
    public bool SeedDemoData { get; set; } = true;
    public int SeedHistoryDays { get; set; } = 14;
    public bool UseMigrations { get; set; } = false;
    public bool RecreateIfSchemaMismatch { get; set; } = false;
}

/// <summary>Name shown in the portal header and login pages (appsettings.json -> "Institution").</summary>
public class InstitutionOptions
{
    public string Name { get; set; } = "Institution Name";
    public string ShortName { get; set; } = "AttendX";
    public string SystemName { get; set; } = "Attendance Management System";
}
