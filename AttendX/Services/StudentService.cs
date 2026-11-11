using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AttendX.Services;

public interface IStudentService
{
    Task<(Student? Student, string? Error)> CreateAsync(StudentFormViewModel m, bool audit = true);
    Task<(bool Ok, string? Error)> UpdateAsync(int id, StudentFormViewModel m);
    Task<(bool Ok, string? Error)> SetActiveAsync(int id, bool active);
    Task<ImportResult> ImportCsvAsync(string csv);
    Task<int> GenerateQrAsync(IEnumerable<int> studentIds, bool regenerateExisting);
    Task<string?> ValidateAcademicAsync(int branchId, int sectionId, int semester);
}

public class StudentService(AppDbContext db, IStudentIdService ids, IQrService qr, IPhotoService photos, IAuditService audit, UserManager<ApplicationUser> users, IOptions<AttendXOptions> o) : IStudentService
{
    // one student is created at a time inside this process, DB unique index protects across processes
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AppDbContext _db = db;
    private readonly IStudentIdService _ids = ids;
    private readonly IQrService _qr = qr;
    private readonly IPhotoService _photos = photos;
    private readonly IAuditService _audit = audit;
    private readonly UserManager<ApplicationUser> _users = users;
    private readonly AttendXOptions _o = o.Value;

    /// <summary>Creates the student's login (email + Institution ID + default password, must change at first login).</summary>
    private async Task EnsureLoginAsync(Student s)
    {
        if (s.IdentityUserId != null) return;
        if (await _users.FindByEmailAsync(s.Email) != null) return;      // email already used by another account - no login created
        var u = new ApplicationUser { UserName = s.Email, Email = s.Email, EmailConfirmed = true, FullName = s.FullName, MustChangePassword = true, LockoutEnabled = true };
        var r = await _users.CreateAsync(u, _o.EffectiveSeedStudentPassword);
        if (!r.Succeeded) return;
        await _users.AddToRoleAsync(u, RoleNames.Student);
        s.IdentityUserId = u.Id;
        await _db.SaveChangesAsync();
    }

    /// <summary>Server-side check that Branch -> Section -> Semester are consistent (never trust the dropdown).</summary>
    public async Task<string?> ValidateAcademicAsync(int branchId, int sectionId, int semester)
    {
        if (semester is < 1 or > 8) return "Semester must be between 1 and 8.";
        var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId);
        if (branch == null || !branch.IsActive) return "Selected branch is invalid or inactive.";
        var section = await _db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectionId);
        if (section == null || !section.IsActive) return "Selected section is invalid or inactive.";
        if (section.BranchId != branchId) return "The selected section does not belong to the selected branch.";
        return null;
    }

    public async Task<(Student? Student, string? Error)> CreateAsync(StudentFormViewModel m, bool audit = true)
    {
        var err = await ValidateAcademicAsync(m.BranchId, m.SectionId, m.Semester);
        if (err != null) return (null, err);
        var email = m.Email.Trim();
        if (await _db.Students.AnyAsync(s => s.IsActive && s.Email == email))
            return (null, "An active student with this email already exists.");

        var (photoPath, photoErr) = await _photos.SaveAsync(m.Photo, m.WebcamPhoto);
        if (photoErr != null) return (null, photoErr);

        var s = new Student
        {
            FullName = m.FullName.Trim(), Phone = m.Phone.Trim(),
            BranchId = m.BranchId, SectionId = m.SectionId, Semester = m.Semester,
            Year = YearHelper.FromSemester(m.Semester),          // never taken from the form
            JoiningDate = m.JoiningDate.Date, PhotoPath = photoPath, IsActive = true
        };

        await Gate.WaitAsync();
        try
        {
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                s.StudentID = await _ids.GenerateAsync(s.Semester, s.SectionId, s.BranchId);
                s.Email = $"{m.FullName.Trim().ToLowerInvariant().Replace(" ", ".")}.{s.StudentID}@attendx.edu.in";
                _db.Students.Add(s);
                try { await _db.SaveChangesAsync(); break; }
                catch (DbUpdateConcurrencyException) { _db.Entry(s).State = EntityState.Detached; s.Id = 0; }
                catch (DbUpdateException) when (attempt < 5) { _db.Entry(s).State = EntityState.Detached; s.Id = 0; }
            }
        }
        finally { Gate.Release(); }

        if (s.Id == 0) return (null, "Could not generate a unique Student ID. Please try again.");

        s.QRPath = await _qr.GenerateStudentQrAsync(s);
        await _db.SaveChangesAsync();
        await EnsureLoginAsync(s);
        if (audit)
        {
            await _audit.LogAsync("StudentCreated", "Student", s.StudentID, null, Snapshot(s), branchId: s.BranchId);
            await _audit.LogAsync("QrGenerated", "Student", s.StudentID, branchId: s.BranchId);
        }
        return (s, null);
    }

    public async Task<(bool Ok, string? Error)> UpdateAsync(int id, StudentFormViewModel m)
    {
        var s = await _db.Students.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return (false, "Student not found.");
        var err = await ValidateAcademicAsync(m.BranchId, m.SectionId, m.Semester);
        if (err != null) return (false, err);
        var email = m.Email.Trim();
        if (await _db.Students.AnyAsync(x => x.IsActive && x.Email == email && x.Id != id))
            return (false, "Another active student already uses this email.");

        var (photoPath, photoErr) = await _photos.SaveAsync(m.Photo, m.WebcamPhoto);
        if (photoErr != null) return (false, photoErr);

        var old = Snapshot(s);
        bool idChange = s.BranchId != m.BranchId || s.SectionId != m.SectionId || s.Semester != m.Semester;

        if (s.IdentityUserId != null && !string.Equals(s.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var lu = await _users.FindByIdAsync(s.IdentityUserId);
            var clash = await _users.FindByEmailAsync(email);
            if (lu != null && (clash == null || clash.Id == lu.Id)) { await _users.SetEmailAsync(lu, email); await _users.SetUserNameAsync(lu, email); }
        }
        s.FullName = m.FullName.Trim(); s.Email = email; s.Phone = m.Phone.Trim();
        s.BranchId = m.BranchId; s.SectionId = m.SectionId; s.Semester = m.Semester;
        s.Year = YearHelper.FromSemester(m.Semester);
        s.JoiningDate = m.JoiningDate.Date;
        s.UpdatedAt = DateTime.UtcNow;
        if (photoPath != null)
        {
            s.PhotoPath = photoPath;
        }
        await Gate.WaitAsync();
        try
        {
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                if (idChange) s.StudentID = await _ids.GenerateAsync(s.Semester, s.SectionId, s.BranchId);
                try { await _db.SaveChangesAsync(); break; }
                catch (DbUpdateConcurrencyException) { return (false, "This student was changed by someone else. Reload and try again."); }
                catch (DbUpdateException) when (attempt < 5 && idChange) { }
            }
        }
        finally { Gate.Release(); }

        if (idChange || string.IsNullOrEmpty(s.QRPath))
        {
            s.QRPath = await _qr.GenerateStudentQrAsync(s);   // new ID => new signed QR (old QR becomes invalid)
            await _db.SaveChangesAsync();
            await _audit.LogAsync("QrGenerated", "Student", s.StudentID, branchId: s.BranchId);
        }
        await _audit.LogAsync("StudentUpdated", "Student", s.StudentID, old, Snapshot(s), branchId: s.BranchId);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetActiveAsync(int id, bool active)
    {
        var s = await _db.Students.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return (false, "Student not found.");
        if (s.IsActive == active) return (true, null);

        if (active)
        {
            if (await _db.Students.AnyAsync(x => x.IsActive && x.StudentID == s.StudentID && x.Id != id))
            {
                s.StudentID = await _ids.GenerateAsync(s.Semester, s.SectionId, s.BranchId);
                s.QRPath = null;
            }
            var expectedEmail = $"{s.FullName.Trim().ToLowerInvariant().Replace(" ", ".")}.{s.StudentID}@attendx.edu.in";
            if (!string.Equals(s.Email, expectedEmail, StringComparison.OrdinalIgnoreCase)) s.Email = expectedEmail;
        }
        s.IsActive = active;
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        if (s.IdentityUserId != null)
        {
            var u = await _users.FindByIdAsync(s.IdentityUserId);
            if (u != null)
            {
                await _users.SetLockoutEnabledAsync(u, true);
                await _users.SetLockoutEndDateAsync(u, active ? null : DateTimeOffset.MaxValue);
                if (active && !string.Equals(u.Email, s.Email, StringComparison.OrdinalIgnoreCase))
                {
                    var clash = await _users.FindByEmailAsync(s.Email);
                    if (clash == null || clash.Id == u.Id)
                    {
                        await _users.SetEmailAsync(u, s.Email);
                        await _users.SetUserNameAsync(u, s.Email);
                        u.FullName = s.FullName;
                        await _users.UpdateAsync(u);
                    }
                }
            }
        }
        if (active && string.IsNullOrEmpty(s.QRPath))
        {
            s.QRPath = await _qr.GenerateStudentQrAsync(s);
            await _db.SaveChangesAsync();
        }
        await _audit.LogAsync(active ? "StudentReactivated" : "StudentDeactivated", "Student", s.StudentID, branchId: s.BranchId);
        return (true, null);
    }

    public async Task<int> GenerateQrAsync(IEnumerable<int> studentIds, bool regenerateExisting)
    {
        int n = 0;
        var ids = studentIds.ToList();
        foreach (var chunk in ids.Chunk(100))
        {
            var list = await _db.Students.Where(s => chunk.Contains(s.Id)).ToListAsync();
            foreach (var s in list)
            {
                if (!regenerateExisting && (!string.IsNullOrEmpty(s.QRPath) && s.QRPath != "on-demand")) continue;
                s.QRPath = await _qr.GenerateStudentQrAsync(s);
                n++;
            }
            await _db.SaveChangesAsync();
        }
        if (n > 0) await _audit.LogAsync("QrGenerated", "Student", null, details: $"{n} QR code(s) generated/regenerated in bulk");
        return n;
    }

    /// <summary>CSV columns: FullName,Email,Phone,BranchCode,Section,Semester,JoiningDate</summary>
    public async Task<ImportResult> ImportCsvAsync(string csv)
    {
        var result = new ImportResult();
        var rows = CsvParser.ParseWithHeader(csv);
        var branches = await _db.Branches.AsNoTracking().ToListAsync();
        var sections = await _db.Sections.AsNoTracking().ToListAsync();
        int line = 1;
        foreach (var r in rows)
        {
            line++; result.Total++;
            string Get(string k) => r.TryGetValue(k, out var v) ? v : "";
            var bkey = Get("branchcode").Length > 0 ? Get("branchcode") : Get("branch");
            var branch = branches.FirstOrDefault(b => b.Code.Equals(bkey, StringComparison.OrdinalIgnoreCase) || b.Name.Equals(bkey, StringComparison.OrdinalIgnoreCase));
            if (branch == null) { result.Errors.Add($"Line {line}: unknown branch '{bkey}'."); continue; }
            var section = sections.FirstOrDefault(s => s.BranchId == branch.Id && s.Name.Equals(Get("section"), StringComparison.OrdinalIgnoreCase));
            if (section == null) { result.Errors.Add($"Line {line}: section '{Get("section")}' not found in {branch.Code}."); continue; }
            if (!int.TryParse(Get("semester"), out var sem)) { result.Errors.Add($"Line {line}: invalid semester."); continue; }
            var join = DateTime.TryParse(Get("joiningdate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var jd) ? jd : DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

            var vm = new StudentFormViewModel { FullName = Get("fullname"), Email = Get("email"), Phone = Get("phone"), BranchId = branch.Id, SectionId = section.Id, Semester = sem, JoiningDate = join };
            var ctx = new ValidationContext(vm);
            var vr = new List<ValidationResult>();
            if (!Validator.TryValidateObject(vm, ctx, vr, true)) { result.Errors.Add($"Line {line}: {vr.First().ErrorMessage}"); continue; }

            var (st, err) = await CreateAsync(vm, audit: false);
            if (st == null) { result.Errors.Add($"Line {line}: {err}"); continue; }
            result.Imported++;
        }
        await _audit.LogAsync("StudentsImported", "Student", null, details: $"CSV import: {result.Imported}/{result.Total} imported, {result.Errors.Count} failed");
        return result;
    }

    private static string Snapshot(Student s) => JsonSerializer.Serialize(new { s.StudentID, s.FullName, s.Email, s.Phone, s.BranchId, s.SectionId, s.Semester, s.Year, s.IsActive });
}
