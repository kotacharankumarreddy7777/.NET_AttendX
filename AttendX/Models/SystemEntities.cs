namespace AttendX.Models;

public enum LocationRequirement { Disabled = 0, Optional = 1, Required = 2 }

/// <summary>Single-row table (Id = 1) with admin configurable rules. Initial values come from appsettings.json.</summary>
public class AttendanceSettings
{
    public int Id { get; set; }
    [Range(0, 120)] public int GraceMinutes { get; set; } = 10;
    public decimal DefaultThreshold { get; set; } = 75;
    [Range(10, 300)] public int QrExpirySeconds { get; set; } = 30;
    public LocationRequirement LocationRequirement { get; set; } = LocationRequirement.Optional;
    /// <summary>0..1 : how much a Late counts as present in percentage calculations (1 = same as Present).</summary>
    public decimal LateWeight { get; set; } = 1m;
    /// <summary>Scans after (period start + this many minutes) are stored as Late. 0 disables.</summary>
    [Range(0, 120)] public int LateAfterMinutes { get; set; } = 15;
    public bool OfflineSyncEnabled { get; set; } = true;
    [Range(0, 1440)] public int OfflineMaxDelayMinutes { get; set; } = 120;
    [Range(1, 12)] public int PeriodCount { get; set; } = 7;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Attendance threshold for a branch / semester / section. Most specific matching rule wins.</summary>
public class ThresholdRule
{
    public int Id { get; set; }
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public int? Semester { get; set; }
    public int? SectionId { get; set; }
    public Section? Section { get; set; }
    public decimal Percent { get; set; } = 75;
    [StringLength(200)] public string? Note { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [StringLength(450)] public string? ActorId { get; set; }
    [StringLength(150)] public string ActorName { get; set; } = "";
    [Required, StringLength(60)] public string Action { get; set; } = "";
    [StringLength(60)] public string EntityType { get; set; } = "";
    [StringLength(80)] public string? EntityId { get; set; }
    public int? BranchId { get; set; }          // lets HOD see only own department
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    [StringLength(400)] public string? Reason { get; set; }
    public string? Details { get; set; }
    [StringLength(45)] public string? IpAddress { get; set; }
}

public class BackupHistory
{
    public int Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    [StringLength(20)] public string Action { get; set; } = "Backup";   // Backup / Restore
    [StringLength(200)] public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    [StringLength(20)] public string Status { get; set; } = "Success";
    [StringLength(500)] public string? Message { get; set; }
    [StringLength(150)] public string? By { get; set; }
}
