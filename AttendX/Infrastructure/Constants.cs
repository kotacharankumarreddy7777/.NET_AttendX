namespace AttendX.Infrastructure;

/// <summary>Role names used with ASP.NET Core Identity. Students and parents have logins.</summary>
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Hod = "HOD";
    public const string Faculty = "Faculty";
    public const string Student = "Student";
    public const string Parent = "Parent";
    public static readonly string[] All = { Admin, Hod, Faculty, Student, Parent };
}

public static class PolicyNames
{
    public const string AdminOnly = "AdminOnly";
    public const string AdminOrHod = "AdminOrHod";
    public const string AnyStaff = "AnyStaff";
    public const string StudentOnly = "StudentOnly";
    public const string ParentOnly = "ParentOnly";
    public const string StudentOrParent = "StudentOrParent";
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
    public string QrSecret { get; set; } = "CHANGE-ME-dev-only-attendx-qr-signing-secret-2026";
    public string BackupFolder { get; set; } = "";
    public string DeviceApiToken { get; set; } = "attendx-device-token-change-me";
    public string AdminEmail { get; set; } = "admin@attendx.edu.in";
    public string AdminPassword { get; set; } = "Admin@123";
    public string SeedFacultyPassword { get; set; } = "Faculty@123";
    public string SeedStudentPassword { get; set; } = "Student@123";
    public string SeedParentPassword { get; set; } = "Parent@123";
    public bool ForceAdminPasswordChange { get; set; } = false;
    public bool AutoExportExcel { get; set; } = true;
    public bool SeedDemoData { get; set; } = true;
    public int SeedHistoryDays { get; set; } = 14;
    public bool UseMigrations { get; set; } = false;
    public bool RecreateIfSchemaMismatch { get; set; } = true;
}

/// <summary>Name shown in the portal header and login pages (appsettings.json -> "Institution").</summary>
public class InstitutionOptions
{
    public string Name { get; set; } = "Institution Name";
    public string ShortName { get; set; } = "AttendX";
    public string SystemName { get; set; } = "Attendance Management System";
    public double Latitude { get; set; } = 18.5204;   // PVG College, Pune (default)
    public double Longitude { get; set; } = 73.8567;
    public int GeoFenceRadiusMeters { get; set; } = 150;
}

/// <summary>Real notification providers (Email = SendGrid/SMTP, SMS = Twilio). Provider="Log" keeps behavior as-is but still writes NotificationLog.</summary>
public class NotificationOptions
{
    public string Provider { get; set; } = "Log";          // Log | SendGrid | Smtp
    public bool NotifyOnAbsence { get; set; } = true;
    public SendGridOptions SendGrid { get; set; } = new();
    public SmtpOptions Smtp { get; set; } = new();
    public TwilioOptions Twilio { get; set; } = new();
}
public class SendGridOptions
{
    public string ApiKey { get; set; } = "";
    public string FromEmail { get; set; } = "noreply@attendx.edu.in";
    public string FromName { get; set; } = "AttendX";
}
public class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "noreply@attendx.edu.in";
    public string FromName { get; set; } = "AttendX";
}
public class TwilioOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string FromNumber { get; set; } = "";
}
