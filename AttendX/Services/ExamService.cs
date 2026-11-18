namespace AttendX.Services;

public interface IExamService
{
    Task<List<Exam>> GetExamsAsync();
    Task<Exam?> GetExamAsync(int id);
    Task<(bool Ok, string? Error)> CreateExamAsync(Exam exam);
    Task<(bool Ok, string? Error)> UpdateExamAsync(int id, Exam exam);
    Task<(bool Ok, string? Error)> DeleteExamAsync(int id);

    Task<List<ExamTimetable>> GetExamTimetableAsync(int? examId, int? branchId, int? sectionId, int? semester);
    Task<(bool Ok, string? Error)> CreateExamTimetableAsync(ExamTimetable tt);
    Task<(bool Ok, string? Error)> UpdateExamTimetableAsync(int id, ExamTimetable tt);
    Task<(bool Ok, string? Error)> DeleteExamTimetableAsync(int id);

    Task<List<ExamRegistration>> GetRegistrationsAsync(int examId);
    Task<(bool Ok, string? Error)> GenerateRegistrationsAsync(int examId);  // Eligibility gate based on attendance
    Task<(bool Ok, string? Error)> ToggleEligibilityAsync(int registrationId, bool eligible, string? reason);

    Task<List<ExamMark>> GetMarksAsync(int examTimetableId);
    Task<(bool Ok, string? Error)> SaveMarkAsync(int examTimetableId, int studentId, decimal internalMark, decimal externalMark, string? remarks, string? markedBy);

    // ---- Lock / modification workflow (faculty locks after external exam; HOD/Admin allots an edit window) ----
    Task<(bool Ok, string? Error)> SubmitMarksAsync(int examTimetableId, string? submittedBy);        // faculty submits drafts for review
    Task<(bool Ok, string? Error)> LockMarksAsync(int examTimetableId, string? lockedBy);              // HOD/Admin locks (no more edits)
    Task<(bool Ok, string? Error)> RaiseLockQueryAsync(int examTimetableId, string reason, string? raisedBy); // faculty raises after lock
    Task<(bool Ok, string? Error)> ReviewLockQueryAsync(int examTimetableId, bool approve, string? note, DateTime? windowStart, DateTime? windowEnd, string? reviewedBy); // HOD/Admin allots window
    Task<List<ExamMark>> GetLockedMarksAwaitingReviewAsync(int? branchId);                            // HOD/Admin list
    Task<decimal> GetStudentAttendancePercentAsync(int studentId, int branchId, int sectionId, int semester);

    // ---- Invigilation duties for faculty during exam windows ----
    Task<List<ExamTimetable>> GetInvigilationDutiesAsync(int facultyId, DateTime from, DateTime to);

    // ---- Result publishing: after EndDate + 3 days, mark ResultsPublished = true ----
    Task<int> PublishPendingResultsAsync();
}

public class ExamService(AppDbContext db, ICurrentUser user, ISettingsService settings, IAttendanceService attendance) : IExamService
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _user = user;
    private readonly ISettingsService _settings = settings;
    private readonly IAttendanceService _attendance = attendance;

    public async Task<List<Exam>> GetExamsAsync()
        => await _db.Exams.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.StartDate).ToListAsync();

    public async Task<Exam?> GetExamAsync(int id)
        => await _db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);

    public async Task<(bool Ok, string? Error)> CreateExamAsync(Exam exam)
    {
        if (await _db.Exams.AnyAsync(e => e.Name == exam.Name && e.Semester == exam.Semester && e.AcademicYearId == exam.AcademicYearId))
            return (false, "An exam with this name already exists for this semester and year.");
        _db.Exams.Add(exam);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> UpdateExamAsync(int id, Exam exam)
    {
        var existing = await _db.Exams.FindAsync(id);
        if (existing == null) return (false, "Exam not found.");
        existing.Name = exam.Name;
        existing.Type = exam.Type;
        existing.Semester = exam.Semester;
        existing.StartDate = exam.StartDate;
        existing.EndDate = exam.EndDate;
        existing.IsActive = exam.IsActive;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteExamAsync(int id)
    {
        var exam = await _db.Exams.FindAsync(id);
        if (exam == null) return (false, "Exam not found.");
        exam.IsActive = false;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<ExamTimetable>> GetExamTimetableAsync(int? examId, int? branchId, int? sectionId, int? semester)
    {
        var q = _db.ExamTimetables.AsNoTracking().Include(x => x.Exam).Include(x => x.Subject).Include(x => x.Faculty).Include(x => x.Section)
            .Where(x => x.IsActive);
        if (examId != null) q = q.Where(x => x.ExamId == examId);
        if (branchId != null) q = q.Where(x => x.BranchId == branchId);
        if (sectionId != null) q = q.Where(x => x.SectionId == sectionId);
        if (semester != null) q = q.Where(x => x.Semester == semester);
        return await q.OrderBy(x => x.ExamDate).ThenBy(x => x.StartTime).ToListAsync();
    }

    public async Task<(bool Ok, string? Error)> CreateExamTimetableAsync(ExamTimetable tt)
    {
        // Check faculty clash
        var clash = await _db.ExamTimetables.FirstOrDefaultAsync(x =>
            x.IsActive && x.FacultyId == tt.FacultyId && x.ExamDate == tt.ExamDate &&
            x.StartTime < tt.EndTime && tt.StartTime < x.EndTime);
        if (clash != null) return (false, "Faculty already has an exam at this time.");

        // Check section clash
        var sectionClash = await _db.ExamTimetables.FirstOrDefaultAsync(x =>
            x.IsActive && x.SectionId == tt.SectionId && x.ExamDate == tt.ExamDate &&
            x.StartTime < tt.EndTime && tt.StartTime < x.EndTime);
        if (sectionClash != null) return (false, "Section already has an exam at this time.");

        // Check room clash: same room cannot host two exams at the same time on the same date.
        var roomClash = await _db.ExamTimetables.FirstOrDefaultAsync(x =>
            x.IsActive && x.RoomNo == tt.RoomNo && x.ExamDate == tt.ExamDate &&
            x.StartTime < tt.EndTime && tt.StartTime < x.EndTime);
        if (roomClash != null) return (false, "Room is already booked for another exam at this time.");

        _db.ExamTimetables.Add(tt);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> UpdateExamTimetableAsync(int id, ExamTimetable tt)
    {
        var existing = await _db.ExamTimetables.FindAsync(id);
        if (existing == null) return (false, "Exam timetable not found.");
        existing.SubjectId = tt.SubjectId;
        existing.ExamDate = tt.ExamDate;
        existing.StartTime = tt.StartTime;
        existing.EndTime = tt.EndTime;
        existing.RoomNo = tt.RoomNo;
        existing.FacultyId = tt.FacultyId;
        existing.IsActive = tt.IsActive;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> DeleteExamTimetableAsync(int id)
    {
        var tt = await _db.ExamTimetables.FindAsync(id);
        if (tt == null) return (false, "Exam timetable not found.");
        tt.IsActive = false;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<ExamRegistration>> GetRegistrationsAsync(int examId)
        => await _db.ExamRegistrations.AsNoTracking().Include(r => r.Student).Where(r => r.ExamId == examId).OrderBy(r => r.Student!.StudentID).ToListAsync();

    public async Task<(bool Ok, string? Error)> GenerateRegistrationsAsync(int examId)
    {
        var exam = await _db.Exams.FirstOrDefaultAsync(e => e.Id == examId);
        if (exam == null) return (false, "Exam not found.");

        var settings = await _settings.GetAsync();
        var students = await _db.Students.AsNoTracking()
            .Where(s => s.IsActive && s.Semester == exam.Semester)
            .ToListAsync();

        var existingRegs = await _db.ExamRegistrations.AsNoTracking()
            .Where(r => r.ExamId == examId)
            .Select(r => r.StudentId)
            .ToListAsync();

        foreach (var s in students)
        {
            if (existingRegs.Contains(s.Id)) continue;

            var pct = await GetStudentAttendancePercentAsync(s.Id, s.BranchId, s.SectionId, s.Semester);
            var threshold = settings.DefaultThreshold;
            var isEligible = pct >= threshold;
            var reason = isEligible ? null : $"Attendance {pct}% is below required {threshold}%.";

            _db.ExamRegistrations.Add(new ExamRegistration
            {
                ExamId = examId,
                StudentId = s.Id,
                IsEligible = isEligible,
                EligibilityReason = reason
            });
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> ToggleEligibilityAsync(int registrationId, bool eligible, string? reason)
    {
        var reg = await _db.ExamRegistrations.FindAsync(registrationId);
        if (reg == null) return (false, "Registration not found.");
        reg.IsEligible = eligible;
        reg.EligibilityReason = reason;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<ExamMark>> GetMarksAsync(int examTimetableId)
        => await _db.ExamMarks.AsNoTracking().Include(m => m.Student).Where(m => m.ExamTimetableId == examTimetableId).OrderBy(m => m.Student!.StudentID).ToListAsync();

    public async Task<(bool Ok, string? Error)> SaveMarkAsync(int examTimetableId, int studentId, decimal internalMark, decimal externalMark, string? remarks, string? markedBy)
    {
        if (internalMark < 0 || internalMark > 30) return (false, "Internal mark must be between 0 and 30.");
        if (externalMark < 0 || externalMark > 70) return (false, "External mark must be between 0 and 70.");

        var tt = await _db.ExamTimetables.AsNoTracking().Include(x => x.Exam).FirstOrDefaultAsync(x => x.Id == examTimetableId);
        if (tt == null) return (false, "Exam timetable not found.");

        // Marks can only be saved after the exam has ended.
        if (tt.Exam == null) return (false, "Exam not linked to timetable.");
        if (DateTime.UtcNow < tt.Exam.EndDate)
            return (false, $"Marks cannot be entered until the exam ends on {tt.Exam.EndDate:dd MMM yyyy}.");

        var marks = await _db.ExamMarks.Where(m => m.ExamTimetableId == examTimetableId && m.StudentId == studentId).ToListAsync();
        if (marks.Any())
        {
            // Editing is allowed only while not Locked, or inside an approved modification window.
            var m = marks.First();
            if (m.LockStatus == "Locked")
            {
                var now = DateTime.UtcNow;
                bool inWindow = m.ModificationQueryStatus == "Approved"
                                && m.ModificationWindowStart.HasValue && m.ModificationWindowEnd.HasValue
                                && now >= m.ModificationWindowStart.Value && now <= m.ModificationWindowEnd.Value;
                if (!inWindow)
                    return (false, "Marks are locked. Raise a modification query to the HOD/Admin to get an edit window.");
            }
            m.InternalMark = internalMark;
            m.ExternalMark = externalMark;
            m.Remarks = remarks;
            m.MarkedBy = markedBy;
            m.LastModifiedAt = DateTime.UtcNow;
            m.Grade = ComputeGrade(internalMark + externalMark, tt.Exam?.Type == "External");
        }
        else
        {
            _db.ExamMarks.Add(new ExamMark
            {
                ExamTimetableId = examTimetableId,
                StudentId = studentId,
                InternalMark = internalMark,
                ExternalMark = externalMark,
                Remarks = remarks,
                MarkedBy = markedBy,
                MarkedAt = DateTime.UtcNow,
                LockStatus = "Draft",
                Grade = ComputeGrade(internalMark + externalMark, tt.Exam?.Type == "External")
            });
        }
        await _db.SaveChangesAsync();
        return (true, null);
    }

    // ------------------------------------------------------------------ lock / modification workflow
    public async Task<(bool Ok, string? Error)> SubmitMarksAsync(int examTimetableId, string? submittedBy)
    {
        var pending = await _db.ExamMarks.Where(m => m.ExamTimetableId == examTimetableId && m.LockStatus == "Draft").ToListAsync();
        if (pending.Count == 0) return (false, "No draft marks to submit.");
        foreach (var m in pending)
        {
            m.LockStatus = "Submitted";
            m.LastModifiedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> LockMarksAsync(int examTimetableId, string? lockedBy)
    {
        var submitted = await _db.ExamMarks.Where(m => m.ExamTimetableId == examTimetableId && m.LockStatus != "Locked").ToListAsync();
        if (submitted.Count == 0) return (false, "No marks to lock.");
        var now = DateTime.UtcNow;
        foreach (var m in submitted)
        {
            m.LockStatus = "Locked";
            m.LockedAt = now;
            m.LockedBy = lockedBy;
        }
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> RaiseLockQueryAsync(int examTimetableId, string reason, string? raisedBy)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (false, "Reason is required.");
        var marks = await _db.ExamMarks.Where(m => m.ExamTimetableId == examTimetableId && m.LockStatus == "Locked").ToListAsync();
        if (marks.Count == 0) return (false, "No locked marks found for this exam.");
        var now = DateTime.UtcNow;
        foreach (var m in marks)
        {
            m.ModificationQueryStatus = "Pending";
            m.ModificationQueryReason = reason.Trim();
            m.ModificationQueryBy = raisedBy;
            m.ModificationQueryAt = now;
        }
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> ReviewLockQueryAsync(int examTimetableId, bool approve, string? note, DateTime? windowStart, DateTime? windowEnd, string? reviewedBy)
    {
        var marks = await _db.ExamMarks.Where(m => m.ExamTimetableId == examTimetableId && m.ModificationQueryStatus == "Pending").ToListAsync();
        if (marks.Count == 0) return (false, "No pending queries to review.");
        var now = DateTime.UtcNow;
        if (approve)
        {
            if (!windowStart.HasValue || !windowEnd.HasValue || windowEnd <= windowStart)
                return (false, "Provide a valid window (start < end).");
            foreach (var m in marks)
            {
                m.ModificationQueryStatus = "Approved";
                m.ModificationReviewedBy = reviewedBy;
                m.ModificationReviewedAt = now;
                m.ModificationReviewNote = note;
                m.ModificationWindowStart = windowStart;
                m.ModificationWindowEnd = windowEnd;
            }
        }
        else
        {
            foreach (var m in marks)
            {
                m.ModificationQueryStatus = "Rejected";
                m.ModificationReviewedBy = reviewedBy;
                m.ModificationReviewedAt = now;
                m.ModificationReviewNote = note;
            }
        }
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<ExamMark>> GetLockedMarksAwaitingReviewAsync(int? branchId)
    {
        var q = _db.ExamMarks.AsNoTracking()
            .Include(m => m.Student)
            .Include(m => m.ExamTimetable)!.ThenInclude(tt => tt!.Exam)
            .Include(m => m.ExamTimetable)!.ThenInclude(tt => tt!.Subject)
            .Include(m => m.ExamTimetable)!.ThenInclude(tt => tt!.Section)
            .Where(m => m.ModificationQueryStatus == "Pending");
        if (branchId.HasValue) q = q.Where(m => m.ExamTimetable!.BranchId == branchId.Value);
        return await q.OrderBy(m => m.ModificationQueryAt).ToListAsync();
    }

    private static string? ComputeGrade(decimal total, bool isExternal)
    {
        if (!isExternal) return total >= 24 ? "P" : "F";   // internal-only exams: 24/30 pass
        return total switch
        {
            >= 90 => "O",
            >= 80 => "A+",
            >= 70 => "A",
            >= 60 => "B+",
            >= 50 => "B",
            >= 40 => "C",
            >= 35 => "P",
            _ => "F"
        };
    }

    public async Task<decimal> GetStudentAttendancePercentAsync(int studentId, int branchId, int sectionId, int semester)
    {
        var settings = await _settings.GetAsync();
        var rows = await _db.Attendance.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.BranchId == branchId && a.SectionId == sectionId && a.Semester == semester)
            .ToListAsync();
        if (rows.Count == 0) return 100m;
        var present = rows.Count(a => a.Status == AttendanceStatus.Present);
        var late = rows.Count(a => a.Status == AttendanceStatus.Late);
        return AttendanceMath.Percent(present, late, rows.Count, settings.LateWeight);
    }

    // ------------------------------------------------------------------ invigilation duties
    public async Task<List<ExamTimetable>> GetInvigilationDutiesAsync(int facultyId, DateTime from, DateTime to)
    {
        return await _db.ExamTimetables.AsNoTracking()
            .Include(x => x.Exam).Include(x => x.Subject).Include(x => x.Section).Include(x => x.Branch)
            .Where(x => x.IsActive && x.FacultyId == facultyId && x.ExamDate.Date >= from.Date && x.ExamDate.Date <= to.Date)
            .OrderBy(x => x.ExamDate).ThenBy(x => x.StartTime)
            .ToListAsync();
    }

    // ------------------------------------------------------------------ result publishing
    public async Task<int> PublishPendingResultsAsync()
    {
        var now = DateTime.UtcNow;
        var exams = await _db.Exams.Where(e => e.IsActive && !e.ResultsPublished && e.ResultReleaseDate <= now).ToListAsync();
        foreach (var e in exams)
        {
            e.ResultsPublished = true;
        }
        if (exams.Count > 0) await _db.SaveChangesAsync();
        return exams.Count;
    }
}

