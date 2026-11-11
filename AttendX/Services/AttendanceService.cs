using System.Globalization;
using System.Text.Json;

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
}

/// <summary>
/// All attendance business rules live here (never in controllers or JavaScript) so a future REST API / mobile app can reuse them.
/// Rules enforced: logged-in faculty identity, assigned class, same day, valid period window (server clock),
/// student belongs to the class, signed QR, rotating session token, duplicate protection, audit trail.
/// </summary>
public class AttendanceService(AppDbContext db, IClock clock, ICurrentUser user, ISettingsService settings, IQrService qr, IAuditService audit, ICalendarService calendar) : IAttendanceService
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ICurrentUser _user = user;
    private readonly ISettingsService _settings = settings;
    private readonly IQrService _qr = qr;
    private readonly IAuditService _audit = audit;
    private readonly ICalendarService _calendar = calendar;

    // ------------------------------------------------------------------ slots
    public async Task<List<SlotView>> GetSlotsAsync(DateTime date, int? facultyId, int? branchId, int? sectionId)
    {
        var dateUtc = IClock.DateUtc(date);
        var dateIst = SystemClock.IstFromUtc(dateUtc).Date;
        var nowIst = _clock.NowIst;
        var year = await _settings.GetCurrentYearAsync();
        var settings = await _settings.GetAsync();

        var q = _db.Timetables.AsNoTracking().Include(t => t.Subject).Include(t => t.Section).Include(t => t.Branch).Include(t => t.Faculty)
            .Where(t => t.IsActive && t.AcademicYearId == year.Id && t.Day == dateIst.DayOfWeek);
        if (facultyId != null) q = q.Where(t => t.FacultyId == facultyId);
        if (branchId != null) q = q.Where(t => t.BranchId == branchId);
        if (sectionId != null) q = q.Where(t => t.SectionId == sectionId);
        var rows = await q.OrderBy(t => t.Period).ThenBy(t => t.Section != null ? t.Section.Name : "").ToListAsync();
        var filtered = new List<Timetable>();
        foreach (var r in rows)
        {
            int? b = r.BranchId;
            int? sId = r.SectionId;
            int? sem = r.Semester;
            var reason = await _calendar.GetNonWorkingReasonAsync(dateUtc, b, sId, sem);
            if (reason != null) continue;
            filtered.Add(r);
        }
        rows = filtered;

        var ids = rows.Select(r => r.Id).ToList();
        var sessions = await _db.AttendanceSessions.AsNoTracking().Where(s => s.Date == dateUtc && ids.Contains(s.TimetableId))
            .Select(s => new { s.Id, s.TimetableId }).ToListAsync();
        var counts = await _db.Attendance.AsNoTracking().Where(a => a.Date == dateUtc)
            .GroupBy(a => a.AttendanceSessionId).Select(g => new { g.Key, C = g.Count() }).ToListAsync();
        var countBySession = counts.ToDictionary(c => c.Key, c => c.C);

        var list = new List<SlotView>();
        foreach (var t in rows)
        {
            var state = AttendanceWindow.State(nowIst, dateIst, t.StartTime, t.EndTime, settings.GraceMinutes);
            var (_, end) = AttendanceWindow.For(dateIst, t.StartTime, t.EndTime, settings.GraceMinutes);
            var sess = sessions.FirstOrDefault(s => s.TimetableId == t.Id);
            list.Add(new SlotView
            {
                TimetableId = t.Id, Start = t.StartTime, End = t.EndTime, Semester = t.Semester,
                SubjectName = t.Subject!.SubjectName, SubjectCode = t.Subject.SubjectCode,
                SectionName = t.Section!.Name, BranchCode = t.Branch!.Code, FacultyName = t.Faculty!.FullName, Room = t.RoomNo,
                State = state, RemainingMinutes = state == "Open" ? (int)Math.Max(0, (end - nowIst).TotalMinutes) : 0,
                HasSession = sess != null, MarkedCount = sess != null && countBySession.TryGetValue(sess.Id, out var c) ? c : 0
            });
        }
        return list;
    }

    // ------------------------------------------------------------------ access rules
    public async Task<AccessCheck> CheckAccessAsync(Timetable t, DateTime date)
    {
        var todayUtc = _clock.TodayIst;
        var dateUtc = IClock.DateUtc(date);
        var dateIst = SystemClock.IstFromUtc(dateUtc).Date;
        var nowIst = _clock.NowIst;
        var settings = await _settings.GetAsync();
        if (!t.IsActive) return new AccessCheck(AccessOutcome.Inactive, false, "This timetable slot is no longer active.");
        if (dateUtc > todayUtc) return new AccessCheck(AccessOutcome.Future, false, "Attendance cannot be marked for a future date.");
        var nonWorking = await _calendar.GetNonWorkingReasonAsync(dateUtc, t.BranchId, t.SectionId, t.Semester);
        if (nonWorking != null) return new AccessCheck(AccessOutcome.OutsideTime, false, $"No classes on {dateIst:dd MMM yyyy} ({nonWorking}).");

        var me = await _user.GetFacultyAsync();
        bool assigned = me != null && me.Id == t.FacultyId;
        bool open = AttendanceWindow.IsOpen(nowIst, dateIst, t.StartTime, t.EndTime, settings.GraceMinutes);

        if (assigned && dateUtc == todayUtc)
        {
            if (!open && dateUtc == todayUtc)
            {
                var hasRecords = await _db.AttendanceSessions.AsNoTracking().Where(s => s.TimetableId == t.Id && s.Date == dateUtc).Select(s => s.Id).ToListAsync();
                bool hasMarks = hasRecords.Count > 0 && await _db.Attendance.AsNoTracking().AnyAsync(a => hasRecords.Contains(a.AttendanceSessionId));
                if (!hasMarks) return new AccessCheck(AccessOutcome.Ok, false, "OK");
                if (nowIst.TimeOfDay < t.StartTime) return new AccessCheck(AccessOutcome.Ok, false, "OK");
                return new AccessCheck(AccessOutcome.Ok, true, "OK");
            }
            return new AccessCheck(AccessOutcome.Ok, false, "OK");
        }
        if (_user.IsAdmin) return new AccessCheck(AccessOutcome.Ok, !open, "OK");
        if (_user.IsHod)
        {
            var scope = await _user.GetScopeBranchIdAsync();
            if (scope == t.BranchId) return new AccessCheck(AccessOutcome.Ok, !open, "OK");
        }
        if (assigned)
        {
            if (dateUtc < todayUtc)
                return new AccessCheck(AccessOutcome.OutsideTime, false,
                    "To correct past attendance, send an AttendanceQuery to your branch HOD for approval.");
            var (s, e) = AttendanceWindow.For(dateIst, t.StartTime, t.EndTime, settings.GraceMinutes);
            return new AccessCheck(AccessOutcome.OutsideTime, false,
                $"Outside the attendance window ({s:hh:mm tt}-{e:hh:mm tt} on {dateIst:dd MMM yyyy}). After the period only Admin/HOD can correct attendance.");
        }
        return new AccessCheck(AccessOutcome.Unauthorized, false, "You are not authorised to mark attendance for this class.");
    }

    // ------------------------------------------------------------------ mark page
    public async Task<(MarkPageViewModel? Page, string? Error)> BuildMarkPageAsync(int timetableId, DateTime date)
    {
        var dateUtc = IClock.DateUtc(date);
        var dateIst = SystemClock.IstFromUtc(dateUtc).Date;
        var t = await _db.Timetables.AsNoTracking().Include(x => x.Subject).Include(x => x.Section).Include(x => x.Branch).Include(x => x.Faculty)
            .FirstOrDefaultAsync(x => x.Id == timetableId);
        if (t == null) return (null, "Class not found.");
        var access = await CheckAccessAsync(t, dateUtc);
        if (!access.Allowed) return (null, access.Message);

        var settings = await _settings.GetAsync();
        var students = await _db.Students.AsNoTracking()
            .Where(s => s.IsActive && s.SectionId == t.SectionId && s.BranchId == t.BranchId && s.Semester == t.Semester)
            .OrderBy(s => s.StudentID).ToListAsync();
        var sids = students.Select(s => s.Id).ToList();
        var existing = await _db.Attendance.AsNoTracking()
            .Where(a => a.Date == dateUtc && a.Period == t.Period && sids.Contains(a.StudentId))
            .ToDictionaryAsync(a => a.StudentId, a => a.Status.ToString());

        var (_, end) = AttendanceWindow.For(dateIst, t.StartTime, t.EndTime, settings.GraceMinutes);
        var vm = new MarkPageViewModel
        {
            TimetableId = t.Id, Date = dateUtc, SubjectName = t.Subject?.SubjectName ?? "", SubjectCode = t.Subject?.SubjectCode ?? "",
            SectionName = t.Section?.Name ?? "", BranchCode = t.Branch?.Code ?? "", FacultyName = t.Faculty?.FullName ?? "", Room = t.RoomNo,
            Period = t.Period, Start = t.StartTime, End = t.EndTime, IsCorrection = access.IsCorrection,
            CanScanQr = !access.IsCorrection, OfflineEnabled = settings.OfflineSyncEnabled && !access.IsCorrection,
            LocationRequirement = settings.LocationRequirement.ToString(),
            WindowEndMs = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Unspecified), SystemClock.IstOffset).ToUnixTimeMilliseconds(),
            ServerNowMs = new DateTimeOffset(_clock.NowUtc, TimeSpan.Zero).ToUnixTimeMilliseconds(),
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
        var dateUtc = IClock.DateUtc(date);
        var t = await _db.Timetables.Include(x => x.Subject).FirstOrDefaultAsync(x => x.Id == timetableId);
        if (t == null) return new SaveResult(false, "Class not found.");
        var access = await CheckAccessAsync(t, dateUtc);
        if (!access.Allowed) return new SaveResult(false, access.Message);
        if (access.IsCorrection && string.IsNullOrWhiteSpace(reason))
            return new SaveResult(false, "A reason is required for corrections made after the period has ended.");

        var settings = await _settings.GetAsync();
        if (!access.IsCorrection && settings.LocationRequirement == LocationRequirement.Required && device.Latitude == null)
            return new SaveResult(false, "Location is required by the administrator. Allow location access in your browser and try again.");

        var students = await _db.Students.Where(s => s.IsActive && s.SectionId == t.SectionId && s.BranchId == t.BranchId && s.Semester == t.Semester).ToListAsync();
        var valid = students.ToDictionary(s => s.Id);

        var session = await GetOrCreateSessionAsync(t, dateUtc, "Manual", device);
        var existing = await _db.Attendance.Where(a => a.AttendanceSessionId == session.Id).ToDictionaryAsync(a => a.StudentId);
        var now = _clock.NowUtc;
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
        return new SaveResult(true, $"Attendance saved: {added} new, {updated} changed, {skipped} unchanged.", added, updated, skipped);
    }

    // ------------------------------------------------------------------ QR session token + scan
    public async Task<(string? Token, int ExpiresIn, string? Error)> IssueSessionTokenAsync(int timetableId)
    {
        var t = await _db.Timetables.FirstOrDefaultAsync(x => x.Id == timetableId);
        if (t == null) return (null, 0, "Class not found.");
        var access = await CheckAccessAsync(t, _clock.TodayIst);
        var me = await _user.GetFacultyAsync();
        if (!access.Allowed || access.IsCorrection || me == null || me.Id != t.FacultyId)
            return (null, 0, access.Allowed && !access.IsCorrection ? "Only the assigned faculty can scan." : access.Message);
        var settings = await _settings.GetAsync();
        var session = await GetOrCreateSessionFlushedAsync(t, _clock.TodayIst, "QR", new DeviceInfo(null, null, null));
        var token = _qr.Signer.CreateSessionToken(session.Id, new DateTimeOffset(_clock.NowUtc, TimeSpan.Zero), settings.QrExpirySeconds);
        return (token, settings.QrExpirySeconds, null);
    }

    public async Task<ScanResult> ScanAsync(int timetableId, string payload, string token, DeviceInfo device)
    {
        var todayUtc = _clock.TodayIst;
        var dateIst = SystemClock.IstFromUtc(todayUtc).Date;
        var nowIst = _clock.NowIst;
        var nowUtc = _clock.NowUtc;
        var settings = await _settings.GetAsync();
        var t = await _db.Timetables.FirstOrDefaultAsync(x => x.Id == timetableId);
        if (t == null) return new ScanResult(ScanCode.WrongClass, "Class not found.");

        var me = await _user.GetFacultyAsync();
        if (me == null || me.Id != t.FacultyId || !t.IsActive)
            return new ScanResult(ScanCode.Unauthorized, "You are not the assigned faculty for this class.");
        if (!AttendanceWindow.IsOpen(nowIst, dateIst, t.StartTime, t.EndTime, settings.GraceMinutes))
            return new ScanResult(ScanCode.OutsideTime, "Outside the attendance time window for this period.");
        if (settings.LocationRequirement == LocationRequirement.Required && device.Latitude == null)
            return new ScanResult(ScanCode.LocationRequired, "Location is required. Allow location access in your browser.");

        var session = await GetOrCreateSessionFlushedAsync(t, todayUtc, "QR", device);
        if (!_qr.Signer.ValidateSessionToken(session.Id, token, new DateTimeOffset(_clock.NowUtc, TimeSpan.Zero), settings.QrExpirySeconds))
            return new ScanResult(ScanCode.SessionExpired, "Scan session expired. The page is refreshing the session key - scan again.");

        if (!_qr.Signer.TryParseStudentPayload(payload, out var studentId, out var code))
            return new ScanResult(ScanCode.InvalidQr, "Invalid or tampered QR code.");
        var st = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId);
        if (st == null || !st.IsActive || st.StudentID != code)
            return new ScanResult(ScanCode.InvalidQr, "This QR does not match an active student (old or replaced card).");
        if (st.SectionId != t.SectionId || st.BranchId != t.BranchId || st.Semester != t.Semester)
            return new ScanResult(ScanCode.WrongClass, $"{st.FullName} ({st.StudentID}) does not belong to this class.", st.FullName, st.StudentID);

        return await RecordAsync(t, st, DecideStatus(t, dateIst, nowIst, settings), "QR", device, nowUtc, session);
    }

    // ------------------------------------------------------------------ offline sync
    public async Task<List<SyncResult>> SyncOfflineAsync(List<OfflineRecord> records)
    {
        var results = new List<SyncResult>();
        var settings = await _settings.GetAsync();
        var todayUtc = _clock.TodayIst;
        var nowIst = _clock.NowIst;
        var nowUtc = _clock.NowUtc;
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
                if (!DateTime.TryParse(rec.Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate)) { results.Add(new(rec.ClientId, false, "Invalid date.")); continue; }
                var date = IClock.DateUtc(parsedDate);

                var capturedUtc = rec.CapturedAt.Kind == DateTimeKind.Utc ? rec.CapturedAt : rec.CapturedAt.Kind == DateTimeKind.Local ? TimeZoneInfo.ConvertTimeToUtc(rec.CapturedAt) : DateTime.SpecifyKind(rec.CapturedAt, DateTimeKind.Utc);
                var capturedIst = SystemClock.IstFromUtc(capturedUtc);
                if (capturedUtc > nowUtc.AddMinutes(2)) { results.Add(new(rec.ClientId, false, "Record time is in the future.")); continue; }
                var capturedDateUtc = IClock.DateUtc(capturedIst);
                if (capturedDateUtc != date || date > todayUtc) { results.Add(new(rec.ClientId, false, "Record date mismatch.")); continue; }
                if (!AttendanceWindow.IsOpen(capturedIst, capturedIst.Date, t.StartTime, t.EndTime, settings.GraceMinutes))
                { results.Add(new(rec.ClientId, false, "Recorded outside the period's attendance window.")); continue; }
                var syncDeadlineIst = SystemClock.IstFromUtc(IClock.DateUtc(date)) + t.EndTime + TimeSpan.FromMinutes(settings.GraceMinutes + settings.OfflineMaxDelayMinutes);
                if (nowIst > syncDeadlineIst) { results.Add(new(rec.ClientId, false, $"Too late to sync (limit: {settings.OfflineMaxDelayMinutes} min after the period).")); continue; }

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
                if (fromQr) status = DecideStatus(t, capturedIst.Date, capturedIst, settings);
                else if (!Enum.TryParse(rec.Status, true, out status) || !Enum.IsDefined(status)) { results.Add(new(rec.ClientId, false, "Invalid status.")); continue; }

                var session = await GetOrCreateSessionFlushedAsync(t, date, "Offline", new DeviceInfo(rec.DeviceId, null, null));
                var r = await RecordAsync(t, st, status, "Offline", new DeviceInfo(rec.DeviceId, null, null), capturedUtc, session);
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
        return new ScanResult(ScanCode.Marked, $"Attendance marked: {st.FullName} ({status}).", st.FullName, st.StudentID, status.ToString());
    }

    private async Task<AttendanceSession> GetOrCreateSessionAsync(Timetable t, DateTime date, string mode, DeviceInfo dev)
    {
        var s = await _db.AttendanceSessions.FirstOrDefaultAsync(x => x.TimetableId == t.Id && x.Date == date);
        if (s != null) return s;
        s = new AttendanceSession
        {
            TimetableId = t.Id, AcademicYearId = t.AcademicYearId, Date = date, Period = t.Period, SubjectId = t.SubjectId, SectionId = t.SectionId,
            FacultyId = t.FacultyId, Mode = mode, DeviceId = dev.DeviceId, StartedByUserId = _user.UserId, StartedAt = _clock.NowUtc,
            Latitude = dev.Latitude == null ? null : Math.Round(dev.Latitude.Value, 3),
            Longitude = dev.Longitude == null ? null : Math.Round(dev.Longitude.Value, 3)
        };
        _db.AttendanceSessions.Add(s);
        return s;
    }

    private async Task<AttendanceSession> GetOrCreateSessionFlushedAsync(Timetable t, DateTime date, string mode, DeviceInfo dev)
    {
        var s = await GetOrCreateSessionAsync(t, date, mode, dev);
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var existing = await _db.AttendanceSessions.FirstOrDefaultAsync(x => x.TimetableId == t.Id && x.Date == date);
            if (existing != null) return existing;
            throw;
        }
        return s;
    }

    /// <summary>Adds an audit row to the SAME unit of work as the attendance change, so both are saved (or fail) together.</summary>
    private void AddAudit(string action, string entityId, Timetable t, string? oldV, string? newV, string? reason, string? details)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Timestamp = _clock.NowUtc, ActorId = _user.UserId, ActorName = _user.DisplayName, Action = action, EntityType = "Attendance",
            EntityId = entityId, BranchId = t.BranchId, OldValues = oldV, NewValues = newV, Reason = reason, Details = details, IpAddress = _user.IpAddress
        });
    }
}
