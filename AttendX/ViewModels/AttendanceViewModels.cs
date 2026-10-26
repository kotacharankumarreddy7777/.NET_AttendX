namespace AttendX.ViewModels;

public record DeviceInfo(string? DeviceId, decimal? Latitude, decimal? Longitude);

public enum AccessOutcome { Ok, Unauthorized, OutsideTime, Future, Inactive }

public record AccessCheck(AccessOutcome Outcome, bool IsCorrection, string Message)
{
    public bool Allowed => Outcome == AccessOutcome.Ok;
}

public record SaveResult(bool Ok, string Message, int Added = 0, int Updated = 0, int Skipped = 0);

public enum ScanCode { Marked, AlreadyMarked, InvalidQr, WrongClass, OutsideTime, Unauthorized, SessionExpired, LocationRequired }

public record ScanResult(ScanCode Code, string Message, string? StudentName = null, string? StudentCode = null, string? Status = null);

/// <summary>A queued (offline) attendance record sent by the browser when connectivity returns.</summary>
public class OfflineRecord
{
    public string ClientId { get; set; } = "";
    public int TimetableId { get; set; }
    public string Date { get; set; } = "";
    public DateTime CapturedAt { get; set; }
    public int? StudentId { get; set; }
    public string? QrPayload { get; set; }
    public string? Status { get; set; }
    public string? DeviceId { get; set; }
}

public record SyncResult(string ClientId, bool Accepted, string Message);

public class SlotView
{
    public int TimetableId { get; set; }
    public int Period { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public string SubjectName { get; set; } = "";
    public string SubjectCode { get; set; } = "";
    public string SectionName { get; set; } = "";
    public string BranchCode { get; set; } = "";
    public string FacultyName { get; set; } = "";
    public string Room { get; set; } = "";
    public int Semester { get; set; }
    public string State { get; set; } = "Upcoming";    // Upcoming / Open / Closed
    public int RemainingMinutes { get; set; }
    public int MarkedCount { get; set; }
    public bool HasSession { get; set; }
    public string? MyStatus { get; set; }
}

public class MarkStudentRow
{
    public int Id { get; set; }
    public string StudentID { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? PhotoPath { get; set; }
    public string? CurrentStatus { get; set; }
}

public class MarkPageViewModel
{
    public int TimetableId { get; set; }
    public DateTime Date { get; set; }
    public string SubjectName { get; set; } = "";
    public string SubjectCode { get; set; } = "";
    public string SectionName { get; set; } = "";
    public string BranchCode { get; set; } = "";
    public string FacultyName { get; set; } = "";
    public string Room { get; set; } = "";
    public int Period { get; set; }
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public bool IsCorrection { get; set; }
    public bool CanScanQr { get; set; }
    public bool OfflineEnabled { get; set; }
    public string LocationRequirement { get; set; } = "Optional";
    public long WindowEndMs { get; set; }      // server window end (ms since epoch) for the countdown
    public long ServerNowMs { get; set; }
    public List<MarkStudentRow> Students { get; set; } = new();
}

public class CorrectionPickerViewModel
{
    public DateTime Date { get; set; } = DateTime.Today;
    public int? BranchId { get; set; }
    public int? SectionId { get; set; }
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Branches { get; set; } = new();
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Sections { get; set; } = new();
    public List<SlotView> Slots { get; set; } = new();
}
