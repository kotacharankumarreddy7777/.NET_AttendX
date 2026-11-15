using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace AttendX.Services;

public interface IAttendanceService
{
    Task<List<SlotView>> GetSlotsAsync(DateTime date, int? facultyId, int? branchId, int? sectionId);
    Task<AccessCheck> CheckAccessAsync(Timetable t, DateTime date);
    Task<(MarkPageViewModel? Page, string? Error)> BuildMarkPageAsync(int timetableId, DateTime date);
    Task<SaveResult> SaveManualAsync(int timetableId, DateTime date, Dictionary<int, string> statuses, string? reason, DeviceInfo device);
    Task<(string? Token, int ExpiresIn, string? Error)> IssueSessionTokenAsync(int timetableId);
    Task<ScanResult> ScanAsync(int timetableId, string payload, string token, DeviceInfo device);
    Task<List<SyncResult>> SyncOfflineAsync(List<OfflineRecord> records);
    Task<int> MarkAbsentAfterGracePeriodAsync();
}

/// <summary>
/// All attendance business rules live here (never in controllers or JavaScript) so a future REST API / mobile app can reuse them.
/// Rules enforced: logged-in faculty identity, assigned class, same day, valid period window (server clock),
/// student belongs to the class, signed QR, rotating session token, duplicate protection, audit trail.
/// </summary>
public class AttendanceService(AppDbContext db, IClock clock, ICurrentUser user, ISettingsService settings, IQrService qr, IAuditService audit, ICalendarService calendar, IHubContext<AttendX.Hubs.AttendanceHub>? hub = null, INotificationService? notification = null, IOptions<InstitutionOptions>? institution = null, ILogger<AttendanceService>? logger = null, IOptions<NotificationOptions>? notificationOptions = null) : IAttendanceService
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ICurrentUser _user = user;
    private readonly ISettingsService _settings = settings;
    private readonly IQrService _qr = qr;
    private readonly IAuditService _audit = audit;
    private readonly ICalendarService _calendar = calendar;
    private readonly INotificationService? _notification = notification;
    private readonly InstitutionOptions _inst = institution?.Value ?? new InstitutionOptions();
    private readonly bool _notifyOnAbsence = notificationOptions?.Value?.NotifyOnAbsence ?? true;
    private readonly ILogger<AttendanceService> _log = logger ?? LoggerFactory.Create(b => b.AddConsole()).CreateLogger<AttendanceService>();

    // ------------------------------------------------------------------ slots
    public async Task<List<SlotView>> GetSlotsAsync(DateTime date, int? facultyId, int? branchId, int? sectionId)
    {
        date = date.Date;
        if (await _calendar.GetNonWorkingReasonAsync(date) != null) return new List<SlotView>();     // Sunday / holiday: no classes
        var now = _clock.Now;
        var year = await _settings.GetCurrentYearAsync();
        var settings = await _settings.GetAsync();

        var q = _db.Timetables.AsNoTracking().Include(t => t.Subject).Include(t => t.Section).Include(t => t.Branch).Include(t => t.Faculty)
            .Where(t => t.IsActive && t.AcademicYearId == year.Id && t.Day == date.DayOfWeek);
        if (facultyId != null) q = q.Where(t => t.FacultyId == facultyId);
        if (branchId != null) q = q.Where(t => t.BranchId == branchId);
        if (sectionId != null) q = q.Where(t => t.SectionId == sectionId);
        var rows = await q.OrderBy(t => t.Period).ThenBy(t => t.Section!.Name).ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();
        var sessions = await _db.AttendanceSessions.AsNoTracking().Where(s => s.Date == date && ids.Contains(s.TimetableId))
            .Select(s => new { s.Id, s.TimetableId }).ToListAsync();
        var counts = await _db.Attendance.AsNoTracking().Where(a => a.Date == date)
            .GroupBy(a => a.AttendanceSessionId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        var countBySession = counts.ToDictionary(c => c.Key, c => c.C);

        var list = new List<SlotView>();
        foreach (var t in rows)
        {
            var state = AttendanceWindow.State(now, date, t.StartTime, t.EndTime, settings.GraceMinutes);
            var (_, end) = AttendanceWindow.For(date, t.StartTime, t.EndTime, settings.GraceMinutes);
            var sess = sessions.FirstOrDefault(s => s.TimetableId == t.Id);
            list.Add(new SlotView
            {
                TimetableId = t.Id, Period = t.Period, Start = t.StartTime, End = t.EndTime, Semester = t.Semester,
                SubjectName = t.Subject!.SubjectName, SubjectCode = t.Subject.SubjectCode,
                SectionName = t.Section!.Name, BranchCode = t.Branch!.Code, FacultyName = t.Faculty!.FullName, Room = t.RoomNo,
                State = state, RemainingMinutes = state == "Open" ? (int)Math.Max(0, (end - now).TotalMinutes) : 0,
                HasSession = sess != null, MarkedCount = sess != null && countBySession.TryGetValue(sess.Id, out var c) ? c : 0
            });
        }
        return list;
    }

    // ------------------------------------------------------------------ access rules
    public async Task<AccessCheck> CheckAccessAsync(Timetable t, DateTime date)
    {
        var now = _clock.Now;
        var settings = await _settings.GetAsync();
        date = date.Date;
        if (!t.IsActive) return new AccessCheck(AccessOutcome.Inactive, false, "This timetable slot is no longer active.");
        if (date > now.Date) return new AccessCheck(AccessOutcome.Future, false, "Attendance cannot be marked for a future date.");
        var nonWorking = await _calendar.GetNonWorkingReasonAsync(date);
        if (nonWorking != null) return new AccessCheck(AccessOutcome.OutsideTime, false, $"No classes on {date:dd MMM yyyy} ({nonWorking}).");

        var me = await _user.GetFacultyAsync();
        bool assigned = me != null && me.Id == t.FacultyId;
        bool open = AttendanceWindow.IsOpen(now, date, t.StartTime, t.EndTime, settings.GraceMinutes);

        if (assigned && date == now.Date) return new AccessCheck(AccessOutcome.Ok, false, "OK");
        if (_user.IsAdmin) return new AccessCheck(AccessOutcome.Ok, !open, "OK");
        if (_user.IsHod)
        {
            var scope = await _user.GetScopeBranchIdAsync();
            if (scope == t.BranchId) return new AccessCheck(AccessOutcome.Ok, !open, "OK");
        }
        if (assigned)
        {
            var (s, e) = AttendanceWindow.For(date, t.StartTime, t.EndTime, settings.GraceMinutes);
            return new AccessCheck(AccessOutcome.OutsideTime, false,
                $"Outside the attendance window ({s:HH:mm}-{e:HH:mm} on {date:dd MMM yyyy}). After the period only Admin/HOD can correct attendance.");
        }
        return new AccessCheck(AccessOutcome.Unauthorized, false, "You are not authorised to mark attendance for this class.");
    }

    // ------------------------------------------------------------------ mark page
    public async Task<(MarkPageViewModel? Page, string? Error)> BuildMarkPageAsync(int timetableId, DateTime date)
    {
        date = date.Date;
        var t = await _db.Timetables.AsNoTracking().Include(x => x.Subject).Include(x => x.Section).Include(x => x.Branch).Include(x => x.Faculty)
            .FirstOrDefaultAsync(x => x.Id == timetableId);
        if (t == null) return (null, "Class not found.");
        var access = await CheckAccessAsync(t, date);
        if (!access.Allowed) return (null, access.Message);

        var settings = await _settings.GetAsync();
        var students = await _db.Students.AsNoTracking()
            .Where(s => s.IsActive && s.SectionId == t.SectionId && s.BranchId == t.BranchId && s.Semester == t.Semester)
            .OrderBy(s => s.StudentID).ToListAsync();
        var sids = students.Select(s => s.Id).ToList();
        var existing = await _db.Attendance.AsNoTracking()
            .Where(a => a.Date == date && a.Period == t.Period && sids.Contains(a.StudentId))
            .ToDictionaryAsync(a => a.StudentId, a => a.Status.ToString());

        var (_, end) = AttendanceWindow.For(date, t.StartTime, t.EndTime, settings.GraceMinutes);
        var vm = new MarkPageViewModel
        {
            TimetableId = t.Id, Date = date, SubjectName = t.Subject!.SubjectName, SubjectCode = t.Subject.SubjectCode,
            SectionName = t.Section!.Name, BranchCode = t.Branch!.Code, FacultyName = t.Faculty!.FullName, Room = t.RoomNo,
            Period = t.Period, Start = t.StartTime, End = t.EndTime, IsCorrection = access.IsCorrection,
            CanScanQr = !access.IsCorrection, OfflineEnabled = settings.OfflineSyncEnabled && !access.IsCorrection,
            LocationRequirement = settings.LocationRequirement.ToString(),
            WindowEndMs = new DateTimeOffset(end).ToUnixTimeMilliseconds(), ServerNowMs = new DateTimeOffset(_clock.Now).ToUnixTimeMilliseconds(),
            Students = [.. students.Select(s => new MarkStudentRow
            {
                Id = s.Id, StudentID = s.StudentID, FullName = s.FullName, PhotoPath = s.PhotoPath,
                CurrentStatus = existing.TryGetValue(s.Id, out var st) ? st : null
            })]
        };
        return (vm, null);
    }

    // ------------------------------------------------------------------ manual marking / correction
    public async Task<SaveResult> SaveManualAsync(int timetableId, DateTime date, Dictionary<int, string> statuses, string? reason, DeviceInfo device)
    {
        date = date.Date;
        var t = await _db.Timetables.Include(x => x.Subject).FirstOrDefaultAsync(x => x.Id == timetableId);
        if (t == null) return new SaveResult(false, "Class not found.");
        var access = await CheckAccessAsync(t, date);
        if (!access.Allowed) return new SaveResult(false, access.Message);
        if (access.IsCorrection && string.IsNullOrWhiteSpace(reason))
            return new SaveResult(false, "A reason is required for corrections made after the period has ended.");

        var settings = await _settings.GetAsync();
        if (!access.IsCorrection)
        {
            bool requireLoc = settings.LocationRequirement == LocationRequirement.Required;
            if (requireLoc && device.Latitude == null)
                return new SaveResult(false, "Location is required by the administrator. Allow location access in your browser and try again.");
            // Geo-fence: enforce campus radius only when the admin has made location mandatory.
            if (requireLoc && device.Latitude != null && GeoFenceBlocks(device, out var geoErr))
                return new SaveResult(false, geoErr!);
        }

        var students = await _db.Students.Where(s => s.IsActive && s.SectionId == t.SectionId && s.BranchId == t.BranchId && s.Semester == t.Semester).ToListAsync();
        var valid = students.ToDictionary(s => s.Id);

        var session = await GetOrCreateSessionAsync(t, date, "Manual", device);
        var existing = await _db.Attendance.Where(a => a.AttendanceSessionId == session.Id).ToDictionaryAsync(a => a.StudentId);
        var now = _clock.Now;
        int added = 0, updated = 0, skipped = 0;

        foreach (var (studentId, raw) in statuses)
        {
            if (!valid.TryGetValue(studentId, out var st) || !Enum.TryParse<AttendanceStatus>(raw, true, out var status) || !Enum.IsDefined(status))
            { skipped++; continue; }                                    // students from other classes / junk values are ignored

            if (existing.TryGetValue(studentId, out var rec))
            {
                if (rec.Status == status) { skipped++; continue; }
                var oldStatus = rec.Status;
                rec.Status = status; rec.LastModifiedAt = now; rec.LastModifiedBy = _user.DisplayName;
                updated++;
                AddAudit(access.IsCorrection ? "AttendanceCorrected" : "AttendanceEdited", st.StudentID, t, oldStatus.ToString(), status.ToString(), reason,
                         $"Subject {t.Subject!.SubjectCode}, {date:yyyy-MM-dd}, period {t.Period}");
            }
            else
            {
                _db.Attendance.Add(new Attendance
                {
                    StudentId = st.Id, SubjectId = t.SubjectId, FacultyId = t.FacultyId, SectionId = t.SectionId, BranchId = st.BranchId, Semester = st.Semester,
                    Date = date, Period = t.Period, Status = status, MarkedAt = now, AttendanceSessionId = session.Id,
                    Source = "Manual", DeviceId = device.DeviceId
                });
                added++;
                if (access.IsCorrection)
                    AddAudit("AttendanceCorrected", st.StudentID, t, "(none)", status.ToString(), reason, $"Subject {t.Subject!.SubjectCode}, {date:yyyy-MM-dd}, period {t.Period}");
            }
        }
        if (added + updated > 0)
            AddAudit("AttendanceSubmitted", $"T{t.Id}", t, null, null, reason, $"{added} new, {updated} changed, {skipped} unchanged on {date:yyyy-MM-dd} period {t.Period}");

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return new SaveResult(false, "Attendance for this class was saved from another tab or device at the same moment. Reload the page to see the latest data.");
        }

        // Push live update to class group + admin-today group
        if (hub != null && (added + updated) > 0)
        {
            try
            {
                await hub.Clients.Group($"class-{t.Id}").SendAsync("AttendanceSaved", new
                {
                    timetableId = t.Id, date = date.ToString("yyyy-MM-dd"), period = t.Period,
                    added, updated, skipped
                });
                await hub.Clients.Group("admin-today").SendAsync("AttendanceSaved", new
                {
                    timetableId = t.Id, date = date.ToString("yyyy-MM-dd"), period = t.Period,
                    branchCode = t.Subject?.SubjectCode ?? "", added, updated, skipped
                });
            }
            catch { /* hub not available (e.g. tests) - non-fatal */ }
        }

        // Fire-and-forget absence alerts (only when configured)
        if (_notification != null && _notifyOnAbsence)
        {
            var absentIds = statuses
                .Where(kv => string.Equals(kv.Value, "Absent", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .Where(id => valid.ContainsKey(id))
                .ToList();
            var subjCode = t.Subject?.SubjectCode ?? "";
            foreach (var sid in absentIds)
            {
                var studentIdCopy = sid;
                _ = Task.Run(async () =>
                {
                    try { await _notification!.SendAbsenceAlertAsync(studentIdCopy, date, subjCode); }
                    catch (Exception ex) { _log.LogError(ex, "Absence alert failed for student {StudentId}", studentIdCopy); }
                });
            }
        }
        return new SaveResult(true, $"Attendance saved: {added} new, {updated} changed, {skipped} unchanged.", added, updated, skipped);
    }

    // ------------------------------------------------------------------ QR session token + scan
    public async Task<(string? Token, int ExpiresIn, string? Error)> IssueSessionTokenAsync(int timetableId)
    {
        var t = await _db.Timetables.FirstOrDefaultAsync(x => x.Id == timetableId);
        if (t == null) return (null, 0, "Class not found.");
        var access = await CheckAccessAsync(t, _clock.Now.Date);
        var me = await _user.GetFacultyAsync();
        if (!access.Allowed || access.IsCorrection || me == null || me.Id != t.FacultyId)
            return (null, 0, access.Allowed && !access.IsCorrection ? "Only the assigned faculty can scan." : access.Message);
        var settings = await _settings.GetAsync();
        var session = await GetOrCreateSessionAsync(t, _clock.Now.Date, "QR", new DeviceInfo(null, null, null));
        var token = _qr.Signer.CreateSessionToken(session.Id, new DateTimeOffset(_clock.Now), settings.QrExpirySeconds);
        return (token, settings.QrExpirySeconds, null);
    }

    public async Task<ScanResult> ScanAsync(int timetableId, string payload, string token, DeviceInfo device)
    {
        var now = _clock.Now;
        var settings = await _settings.GetAsync();
        var t = await _db.Timetables.FirstOrDefaultAsync(x => x.Id == timetableId);
        if (t == null) return new ScanResult(ScanCode.WrongClass, "Class not found.");

        var me = await _user.GetFacultyAsync();
        if (me == null || me.Id != t.FacultyId || !t.IsActive)
            return new ScanResult(ScanCode.Unauthorized, "You are not the assigned faculty for this class.");
        if (!AttendanceWindow.IsOpen(now, now.Date, t.StartTime, t.EndTime, settings.GraceMinutes))
            return new ScanResult(ScanCode.OutsideTime, "Outside the attendance time window for this period.");
        if (settings.LocationRequirement == LocationRequirement.Required)
        {
            if (device.Latitude == null)
                return new ScanResult(ScanCode.LocationRequired, "Location is required. Allow location access in your browser.");
            if (GeoFenceBlocks(device, out var geoErr))
                return new ScanResult(ScanCode.LocationRequired, geoErr!);
        }

        var session = await GetOrCreateSessionAsync(t, now.Date, "QR", device);
        if (!_qr.Signer.ValidateSessionToken(session.Id, token, new DateTimeOffset(now), settings.QrExpirySeconds))
            return new ScanResult(ScanCode.SessionExpired, "Scan session expired. The page is refreshing the session key - scan again.");

        if (!_qr.Signer.TryParseStudentPayload(payload, out var studentId, out var code))
            return new ScanResult(ScanCode.InvalidQr, "Invalid or tampered QR code.");
        var st = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId);
        if (st == null || !st.IsActive || st.StudentID != code)
            return new ScanResult(ScanCode.InvalidQr, "This QR does not match an active student (old or replaced card).");
        if (st.SectionId != t.SectionId || st.BranchId != t.BranchId || st.Semester != t.Semester)
            return new ScanResult(ScanCode.WrongClass, $"{st.FullName} ({st.StudentID}) does not belong to this class.", st.FullName, st.StudentID);

        return await RecordAsync(t, st, DecideStatus(t, now.Date, now, settings), "QR", device, now, session);
    }

    // ------------------------------------------------------------------ offline sync
    public async Task<List<SyncResult>> SyncOfflineAsync(List<OfflineRecord> records)
    {
        var results = new List<SyncResult>();
        var settings = await _settings.GetAsync();
        var now = _clock.Now;
        var me = await _user.GetFacultyAsync();
        var cache = new Dictionary<int, Timetable?>();

        foreach (var rec in records.Take(500))
        {
            try
            {
                if (!settings.OfflineSyncEnabled) { results.Add(new(rec.ClientId, false, "Offline sync is disabled by the administrator.")); continue; }
                if (me == null) { results.Add(new(rec.ClientId, false, "Not authorised.")); continue; }
                if (!cache.TryGetValue(rec.TimetableId, out var t))
                    cache[rec.TimetableId] = t = await _db.Timetables.FirstOrDefaultAsync(x => x.Id == rec.TimetableId);
                if (t == null || !t.IsActive) { results.Add(new(rec.ClientId, false, "Class not found.")); continue; }
                if (t.FacultyId != me.Id) { results.Add(new(rec.ClientId, false, "You are not the assigned faculty for this class.")); continue; }
                if (!DateTime.TryParse(rec.Date, out var date)) { results.Add(new(rec.ClientId, false, "Invalid date.")); continue; }
                date = date.Date;

                var captured = rec.CapturedAt.Kind == DateTimeKind.Utc ? rec.CapturedAt.ToLocalTime() : rec.CapturedAt;
                if (captured > now.AddMinutes(2)) { results.Add(new(rec.ClientId, false, "Record time is in the future.")); continue; }
                if (captured.Date != date || date > now.Date) { results.Add(new(rec.ClientId, false, "Record date mismatch.")); continue; }
                if (!AttendanceWindow.IsOpen(captured, date, t.StartTime, t.EndTime, settings.GraceMinutes))
                { results.Add(new(rec.ClientId, false, "Recorded outside the period's attendance window.")); continue; }
                var syncDeadline = date + t.EndTime + TimeSpan.FromMinutes(settings.GraceMinutes + settings.OfflineMaxDelayMinutes);
                if (now > syncDeadline) { results.Add(new(rec.ClientId, false, $"Too late to sync (limit: {settings.OfflineMaxDelayMinutes} min after the period).")); continue; }

                Student? st = null;
                bool fromQr = false;
                if (!string.IsNullOrEmpty(rec.QrPayload))
                {
                    fromQr = true;
                    if (!_qr.Signer.TryParseStudentPayload(rec.QrPayload, out var sid, out var code)) { results.Add(new(rec.ClientId, false, "Invalid or tampered QR.")); continue; }
                    st = await _db.Students.FirstOrDefaultAsync(s => s.Id == sid);
                    if (st == null || st.StudentID != code) { results.Add(new(rec.ClientId, false, "QR does not match an active student.")); continue; }
                }
                else if (rec.StudentId != null) st = await _db.Students.FirstOrDefaultAsync(s => s.Id == rec.StudentId);

                if (st == null || !st.IsActive) { results.Add(new(rec.ClientId, false, "Student not found or inactive.")); continue; }
                if (st.SectionId != t.SectionId || st.BranchId != t.BranchId || st.Semester != t.Semester)
                { results.Add(new(rec.ClientId, false, $"{st.StudentID} does not belong to this class.")); continue; }

                AttendanceStatus status;
                if (fromQr) status = DecideStatus(t, date, captured, settings);
                else if (!Enum.TryParse(rec.Status, true, out status) || !Enum.IsDefined(status)) { results.Add(new(rec.ClientId, false, "Invalid status.")); continue; }

                var session = await GetOrCreateSessionAsync(t, date, "Offline", new DeviceInfo(rec.DeviceId, null, null));
                var r = await RecordAsync(t, st, status, "Offline", new DeviceInfo(rec.DeviceId, null, null), captured, session);
                results.Add(new(rec.ClientId, r.Code == ScanCode.Marked, r.Code == ScanCode.Marked ? $"{st.StudentID} synced as {status}." : r.Message));
            }
            catch (Exception)
            {
                _db.ChangeTracker.Clear();
                cache.Clear();
                results.Add(new(rec.ClientId, false, "Unexpected server error for this record."));
            }
        }

        int ok = results.Count(r => r.Accepted);
        await _audit.LogAsync("OfflineSync", "Attendance", null, details: $"{ok} accepted, {results.Count - ok} rejected out of {results.Count}");
        return results;
    }

    // ------------------------------------------------------------------ geo-fence
    /// <summary>Returns true if the device is off-campus (outside campus radius). Coords required.</summary>
    private bool GeoFenceBlocks(DeviceInfo dev, out string? error)
    {
        error = null;
        if (dev.Latitude == null || dev.Longitude == null) return false;   // missing coords — not our concern here
        double dist = HaversineMeters(_inst.Latitude, _inst.Longitude, (double)dev.Latitude.Value, (double)dev.Longitude.Value);
        if (dist > _inst.GeoFenceRadiusMeters)
        {
            error = $"Outside campus radius ({dist:0} m > {_inst.GeoFenceRadiusMeters} m).";
            return true;
        }
        return false;
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double dLat = ToRad(lat2 - lat1), dLon = ToRad(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
        static double ToRad(double d) => d * Math.PI / 180.0;
    }

    // ------------------------------------------------------------------ helpers
    private static AttendanceStatus DecideStatus(Timetable t, DateTime date, DateTime at, AttendanceSettings s)
    {
        if (s.LateAfterMinutes > 0 && at > date.Date + t.StartTime + TimeSpan.FromMinutes(s.LateAfterMinutes)) return AttendanceStatus.Late;
        return AttendanceStatus.Present;
    }

    private async Task<ScanResult> RecordAsync(Timetable t, Student st, AttendanceStatus status, string source, DeviceInfo dev, DateTime markedAt, AttendanceSession session)
    {
        bool exists = await _db.Attendance.AnyAsync(a => a.StudentId == st.Id && a.Date == session.Date && a.Period == t.Period);
        if (exists) return new ScanResult(ScanCode.AlreadyMarked, $"{st.FullName} is already marked for this period.", st.FullName, st.StudentID);

        var rec = new Attendance
        {
            StudentId = st.Id, SubjectId = t.SubjectId, FacultyId = t.FacultyId, SectionId = t.SectionId, BranchId = st.BranchId, Semester = st.Semester,
            Date = session.Date, Period = t.Period, Status = status, MarkedAt = markedAt, AttendanceSessionId = session.Id, Source = source, DeviceId = dev.DeviceId
        };
        _db.Attendance.Add(rec);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)      // unique index (StudentId, Date, Period) caught a double scan / second tab
        {
            _db.Entry(rec).State = EntityState.Detached;
            return new ScanResult(ScanCode.AlreadyMarked, $"{st.FullName} is already marked for this period.", st.FullName, st.StudentID);
        }
        // Absence alert for QR-marked absent students
        if (status == AttendanceStatus.Absent && _notification != null && _notifyOnAbsence)
        {
            var subjCode = t.Subject?.SubjectCode ?? "";
            var dateCopy = session.Date;
            _ = Task.Run(async () =>
            {
                try { await _notification!.SendAbsenceAlertAsync(st.Id, dateCopy, subjCode); }
                catch (Exception ex) { _log.LogError(ex, "Absence alert failed for student {StudentId}", st.Id); }
            });
        }
        return new ScanResult(ScanCode.Marked, $"Attendance marked: {st.FullName} ({status}).", st.FullName, st.StudentID, status.ToString());
    }

    private async Task<AttendanceSession> GetOrCreateSessionAsync(Timetable t, DateTime date, string mode, DeviceInfo dev)
    {
        var s = await _db.AttendanceSessions.FirstOrDefaultAsync(x => x.TimetableId == t.Id && x.Date == date);
        if (s != null) return s;
        s = new AttendanceSession
        {
            TimetableId = t.Id, AcademicYearId = t.AcademicYearId, Date = date, Period = t.Period, SubjectId = t.SubjectId, SectionId = t.SectionId,
            FacultyId = t.FacultyId, Mode = mode, DeviceId = dev.DeviceId, StartedByUserId = _user.UserId, StartedAt = _clock.Now,
            Latitude = dev.Latitude == null ? null : Math.Round(dev.Latitude.Value, 3),
            Longitude = dev.Longitude == null ? null : Math.Round(dev.Longitude.Value, 3)
        };
        _db.AttendanceSessions.Add(s);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)         // another request created it first
        {
            _db.Entry(s).State = EntityState.Detached;
            s = await _db.AttendanceSessions.FirstAsync(x => x.TimetableId == t.Id && x.Date == date);
        }
        return s;
    }

    /// <summary>Adds an audit row to the SAME unit of work as the attendance change, so both are saved (or fail) together.</summary>
    private void AddAudit(string action, string entityId, Timetable t, string? oldV, string? newV, string? reason, string? details)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = _clock.Now, ActorId = _user.UserId, ActorName = _user.DisplayName, Action = action, EntityType = "Attendance",
            EntityId = entityId, BranchId = t.BranchId, OldValues = oldV, NewValues = newV, Reason = reason, Details = details, IpAddress = _user.IpAddress
        });
    }

    public async Task<int> MarkAbsentAfterGracePeriodAsync()
    {
        var settings = await _settings.GetAsync();
        var today = _clock.Now.Date;
        var graceEnd = today.AddHours(23).AddMinutes(59); // end of day
        var slots = await _db.Timetables.AsNoTracking()
            .Where(t => t.IsActive && t.Day == today.DayOfWeek)
            .ToListAsync();
        int marked = 0;
        foreach (var t in slots)
        {
            var session = await _db.AttendanceSessions.FirstOrDefaultAsync(s => s.TimetableId == t.Id && s.Date == today);
            if (session == null) continue;
            var alreadyMarked = await _db.Attendance.AsNoTracking().Where(a => a.AttendanceSessionId == session.Id).Select(a => a.StudentId).ToListAsync();
            var students = await _db.Students.AsNoTracking()
                .Where(s => s.IsActive && s.SectionId == t.SectionId && s.BranchId == t.BranchId && s.Semester == t.Semester && !alreadyMarked.Contains(s.Id))
                .ToListAsync();
            foreach (var s in students)
            {
                _db.Attendance.Add(new Attendance
                {
                    StudentId = s.Id, SubjectId = t.SubjectId, FacultyId = t.FacultyId, SectionId = t.SectionId, BranchId = s.BranchId, Semester = s.Semester,
                    Date = today, Period = t.Period, Status = AttendanceStatus.Absent, MarkedAt = graceEnd, AttendanceSessionId = session.Id,
                    Source = "Manual", DeviceId = "auto-absent"
                });
                marked++;
            }
        }
        if (marked > 0) await _db.SaveChangesAsync();
        return marked;
    }
}
