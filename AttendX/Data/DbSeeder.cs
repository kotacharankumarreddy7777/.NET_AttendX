using System.Data;
using System.Globalization;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AttendX.Data;

/// <summary>
/// Creates the database on first run and seeds everything from the Dataset folder (CSV files). Every step only adds what is MISSING.
/// Fast path: users are inserted directly with ONE pre-computed password hash per password (no per-user PBKDF2), students/faculty in batches,
/// attendance history with SqlBulkCopy.  Dataset: subjects, faculty, students, timetable, holidays (CSV) + AttendX_Master_Data.xlsx.
/// </summary>
public class DbSeeder(AppDbContext db, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, IOptions<AttendXOptions> o, IWebHostEnvironment env,
                ISettingsService settings, IMasterDataExportService export, ILogger<DbSeeder> log)
{
    private static readonly (string Name, string Code)[] BranchList = [
        ("CSE", "CSE"), ("CSE-AI", "CSEAI"), ("CSE-AIML", "CSEAIML"), ("AI&DS", "ADS"), ("ECE", "ECE"), ("EEE", "EEE"), ("MECH", "MECH"),
        ("CIVIL", "CIVIL"), ("IT", "IT"), ("Cyber Security", "CYBER"), ("AIDS", "AIDS"), ("AIML", "AIML"), ("IOT", "IOT"), ("DS", "DS")
    ];
    private static readonly Dictionary<string, (string Prefix, int Count)> SectionRules = GenerateSectionRules();
    private static Dictionary<string, (string Prefix, int Count)> GenerateSectionRules()
    {
        var rng = new Random(2026);
        var dict = new Dictionary<string, (string, int)>();
        var prefixes = new Dictionary<string, string>
        {
            ["CSE"] = "EV", ["CSEAI"] = "EN", ["CSEAIML"] = "EN", ["ADS"] = "EU", ["ECE"] = "EC", ["EEE"] = "EE",
            ["MECH"] = "ME", ["CIVIL"] = "CE", ["IT"] = "IT", ["CYBER"] = "CS", ["AIDS"] = "AD", ["AIML"] = "AM", ["IOT"] = "IO", ["DS"] = "DS"
        };
        foreach (var (code, prefix) in prefixes) dict[code] = (prefix, rng.Next(5, 7));
        return dict;
    }
    // BTech branches: 4 years = 8 semesters. Non-BTech: random 6-8 semesters (deterministic per branch code).
    private static int SemestersForBranch(string branchCode)
    {
        var nonBTech = new HashSet<string> { "ADS", "AIDS", "AIML", "DS", "IOT" };
        if (!nonBTech.Contains(branchCode)) return 8;
        var rng = new Random(branchCode.GetHashCode() & 0xffff);
        return rng.Next(6, 9); // 6,7,8
    }
    private static int ClassSizeFor(int branchId, int sectionId, int semester)
    {
        var rng = new Random((branchId * 10000) + (sectionId * 100) + semester);
        return rng.Next(25, 31); // 25-30 inclusive
    }
    private string SeedAdminPassword => _o.EffectiveAdminPassword;
    private string SeedFacultyPassword => _o.EffectiveSeedFacultyPassword;
    private string SeedStudentPassword => _o.EffectiveSeedStudentPassword;
    private string AccountPasswordSuffix => _o.AccountPasswordSuffix;

    private static readonly string[] SampleStudentNames = [
        "Aarav Patel", "Aarav Shah", "Aditi Gupta", "Akash Joshi", "Ananya Jani", "Anjali Chauhan", "Arjun Oza", "Charan Gohil",
        "Darshan Verma", "Deepak Mehta", "Dhruv Nair", "Diya Dave", "Divya Gowda", "Dipti Rao", "Foram Makwana", "Gaurav Bhatt",
        "Harsha Sheth", "Het Mehta", "Hetal Shah", "Isha Dodiya", "Ishaan Kothari", "Ishita Jain", "Janvi Sharma", "Jay Trivedi",
        "Karan Gowda", "Kavya Vyas", "Khushi Dave", "Kiran Kulkarni", "Kiara Thakkar", "Krupa Rao", "Kunal Sharma", "Mansi Dave",
        "Meera Pandya", "Meet Oza", "Mohit Shah", "Naman Raval", "Neha Singh", "Nikhil Thakkar", "Nisha Gupta", "Nidhi Solanki",
        "Om Verma", "Paresh Patel", "Parth Makwana", "Payal Sheth", "Piyush Zala", "Pooja Jain", "Pranali Dodiya", "Pranav Bhatt",
        "Priya Sheth", "Rahul Shah", "Ritu Jain", "Riya Vyas", "Rohit Gohil", "Saanvi Verma", "Sai Kothari", "Shivani Rao",
        "Shreyas Modi", "Shruti Shah", "Siddharth Parmar", "Smit Pandya", "Sonal Barot", "Tanmay Chauhan", "Tanvi Dodiya", "Teja Reddy",
        "Urvi Nair", "Varun Solanki", "Vedant Reddy", "Vidhi Pandya", "Vivaan Solanki", "Yash Goswami", "Yashvi Rathod", "Zoya Bhatt"
    ];

    private readonly AppDbContext _db = db;
    private readonly UserManager<ApplicationUser> _users = users;
    private readonly RoleManager<IdentityRole> _roles = roles;
    private readonly AttendXOptions _o = o.Value;
    private readonly IWebHostEnvironment _env = env;
    private readonly ISettingsService _settings = settings;
    private readonly IMasterDataExportService _export = export;
    private readonly ILogger<DbSeeder> _log = log;
    private readonly bool _usePostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false;

    private string Csv(string name) => Path.Combine(_env.ContentRootPath, "Dataset", name);
    private List<Dictionary<string, string>> Read(string name) => File.Exists(Csv(name)) ? CsvParser.ParseWithHeader(File.ReadAllText(Csv(name))) : [];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await _db.Database.MigrateAsync(cancellationToken);

        foreach (var r in RoleNames.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _roles.RoleExistsAsync(r)) await _roles.CreateAsync(new IdentityRole(r));
        }

        await _settings.GetCurrentYearAsync();
        await _settings.GetAsync();
        await SeedPeriodsAsync(cancellationToken);
        await SeedBranchesAndSectionsAsync(cancellationToken);
        await SeedSubjectsAsync(cancellationToken);
        await FixMalformedStudentIdsAsync(cancellationToken);

        await SeedAdminAsync(cancellationToken);
        if (!_o.SeedDemoData) return;

        // Idempotent check: reseed only what is missing. Never deletes existing data.
        var facultyCsvCount = Read("faculty.csv").Count;
        var studentsCsvCount = Read("students.csv").Count;
        var facultyDbCount = await _db.Faculty.CountAsync(cancellationToken);
        var studentsDbCount = await _db.Students.CountAsync(cancellationToken);
        var attendanceCount = await _db.Attendance.CountAsync(cancellationToken);
        var timetableCount = await _db.Timetables.CountAsync(cancellationToken);

        var needsPeople = !_o.ForceReseed && (facultyDbCount < facultyCsvCount * 0.8 || studentsDbCount < studentsCsvCount * 0.8 || timetableCount == 0);
        var needsAttendance = !_o.ForceReseed && attendanceCount < 5000;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await SeedHolidaysAsync(cancellationToken);
        if (_o.ForceReseed || needsPeople)
        {
            await SeedFacultyAsync(cancellationToken);
            await SeedStudentsAsync(cancellationToken);
            await SeedTimetableAsync(cancellationToken);
        }
        await SeedNoticesAsync(cancellationToken);
        await SeedExamTimetableAsync(cancellationToken);
        if (_o.ForceReseed || needsAttendance)
        {
            await SeedAttendanceHistoryAsync(cancellationToken);
        }
        await SelfCheckAsync(cancellationToken);
        if (watch.Elapsed.TotalSeconds > 5 && _log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeding finished in {Seconds:0} s", watch.Elapsed.TotalSeconds);

        if (_o.AutoExportExcel)
        {
            try
            {
                var path = Path.Combine(_env.ContentRootPath, "App_Data", "Exports", "AttendX_Master_Data.xlsx");
                if (!File.Exists(path) || watch.Elapsed.TotalSeconds > 5) { await _export.SaveToFileAsync(path); if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Master data Excel written to {Path}", path); }
            }
            catch (Exception ex) { _log.LogWarning(ex, "Could not write the master data Excel file."); }
        }
    }

    // ------------------------------------------------------------------ base data

    private async Task SeedPeriodsAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.PeriodDefinitions.AnyAsync()) return;
        var starts = new[] { "09:00", "10:00", "11:00", "12:00", "14:00", "15:00", "16:00" };
        for (int i = 0; i < Math.Min(_o.PeriodCount, starts.Length); i++)
        {
            var s = TimeSpan.Parse(starts[i]);
            var duration = (i == 6) ? 80 : 60; // lab P7 = 80 min, others 60
            _db.PeriodDefinitions.Add(new PeriodDefinition { PeriodNo = i + 1, StartTime = s, EndTime = s.Add(TimeSpan.FromMinutes(duration)) });
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedBranchesAndSectionsAsync(CancellationToken cancellationToken = default)
    {
        var codes = (await _db.Branches.Select(b => b.Code).ToListAsync()).ToHashSet();
        foreach (var (name, code) in BranchList)
            if (!codes.Contains(code)) _db.Branches.Add(new Branch { Name = name, Code = code });
        await _db.SaveChangesAsync();

        var branches = await _db.Branches.ToListAsync();
        var existing = (await _db.Sections.Select(s => new { s.BranchId, s.Name }).ToListAsync()).Select(s => (s.BranchId, s.Name)).ToHashSet();
        foreach (var b in branches)
        {
            if (!SectionRules.TryGetValue(b.Code, out var rule)) continue;
            int sems = SemestersForBranch(b.Code);
            for (int sem = 1; sem <= sems; sem++)
            {
                for (int i = 1; i <= rule.Count; i++)
                {
                    string secName = $"{rule.Prefix}{i}";
                    if (!existing.Contains((b.Id, secName)))
                    {
                        _db.Sections.Add(new Section { BranchId = b.Id, Name = secName });
                        existing.Add((b.Id, secName));
                    }
                }
            }
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken = default)
    {
        var adminEmail = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMIN_EMAIL")) ? Environment.GetEnvironmentVariable("ADMIN_EMAIL")! : _o.AdminEmail;
        if (string.IsNullOrWhiteSpace(SeedAdminPassword)) { _log.LogWarning("Admin seed skipped: AttendX:AdminPassword is empty. Set env ADMIN_PASSWORD."); return; }
        var admin = await _users.FindByEmailAsync(adminEmail);
        if (admin == null)
        {
            admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true, FullName = "System Administrator", MustChangePassword = _o.ForceAdminPasswordChange, LockoutEnabled = true };
            var res = await _users.CreateAsync(admin, SeedAdminPassword);
            if (!res.Succeeded) { _log.LogError("Could not create admin: {Errors}", string.Join("; ", res.Errors.Select(e => e.Description))); return; }
        }
        else
        {
            admin.EmailConfirmed = true;
            admin.MustChangePassword = _o.ForceAdminPasswordChange;
            admin.LockoutEnabled = true;
            admin.LockoutEnd = null;
            admin.AccessFailedCount = 0;
            var hash = new PasswordHasher<ApplicationUser>().HashPassword(admin, SeedAdminPassword);
            admin.PasswordHash = hash;
            var update = await _users.UpdateAsync(admin);
            if (!update.Succeeded) { _log.LogError("Could not update admin: {Errors}", string.Join("; ", update.Errors.Select(e => e.Description))); return; }
        }
        if (!await _users.IsInRoleAsync(admin, RoleNames.Admin)) { var role = await _users.AddToRoleAsync(admin, RoleNames.Admin); if (!role.Succeeded) _log.LogError("Could not assign Admin role: {Errors}", string.Join("; ", role.Errors.Select(e => e.Description))); }
    }

    private async Task SeedSubjectsAsync(CancellationToken cancellationToken = default)
    {
        var rows = Read("subjects.csv");
        if (rows.Count == 0) return;
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Code);
        var codes = (await _db.Subjects.Select(s => s.SubjectCode).ToListAsync()).ToHashSet();
        foreach (var r in rows)
        {
            if (!r.TryGetValue("branchcode", out var bc) || !branches.TryGetValue(bc, out var b)) continue;
            if (!r.TryGetValue("subjectcode", out var sc) || codes.Contains(sc)) continue;
            if (!int.TryParse(r.TryGetValue("semester", out var sv) ? sv : "", out var sem)) continue;
            if (!int.TryParse(r.TryGetValue("credits", out var cv) ? cv : "", out var credits)) continue;
            _db.Subjects.Add(new Subject { BranchId = b.Id, Semester = sem, SubjectCode = sc, SubjectName = r.TryGetValue("subjectname", out var sn) ? sn : sc, Credits = credits });
            codes.Add(sc);
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedHolidaysAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.Holidays.AnyAsync()) return;
        foreach (var r in Read("holidays.csv"))
        {
            if (!DateTime.TryParse(r.TryGetValue("date", out var dv) ? dv : "", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)) continue;
            var dateUtc = DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
            _db.Holidays.Add(new Holiday { Date = dateUtc, Name = r.TryGetValue("name", out var nv) ? nv : "", Type = r.TryGetValue("type", out var tv) ? tv : "Holiday" });
        }
        await _db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ people (fast path)
    private string AccountPassword(string institutionId) => (institutionId ?? string.Empty).Trim() + (!string.IsNullOrWhiteSpace(_o.AccountPasswordSuffix) ? _o.AccountPasswordSuffix : "@AttendX123");
    private string AccountPasswordHash(string institutionId) => new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), AccountPassword(institutionId));

    private static ApplicationUser NewUser(string email, string fullName, string hash, bool mustChangePassword = true) => new()
    {
        Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true, PasswordHash = hash, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(),
        LockoutEnabled = true, FullName = fullName, MustChangePassword = mustChangePassword
    };

    private async Task SeedFacultyAsync(CancellationToken cancellationToken = default)
    {
        var rows = Read("faculty.csv");
        var facultyHash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), SeedFacultyPassword);
        var studentHash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), SeedStudentPassword);
        var roleIds = (await _db.Roles.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(r => r.Name!, r => r.Id, StringComparer.OrdinalIgnoreCase);
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Code);
        var existingEmails = (await _db.Users.Select(u => u.NormalizedEmail).ToListAsync()).ToHashSet();
        var existingFacultyIds = (await _db.Faculty.AsNoTracking().Select(f => f.FacultyID).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (rows.Count > 0)
        {
            if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeding {Count} faculty with logins...", rows.Count);
            int added = 0;
            foreach (var r in rows)
            {
                var fid = r.TryGetValue("facultyid", out var fv) ? fv.Trim() : "";
                if (string.IsNullOrWhiteSpace(fid)) continue;
                if (existingFacultyIds.Contains(fid)) continue;
                if (!branches.TryGetValue(r.TryGetValue("department", out var dv) ? dv.Trim() : "", out var b)) continue;
                var email = EmailHelper.FacultyEmail(fid, r.TryGetValue("fullname", out var fn) ? fn : fid);
                if (existingEmails.Contains(email.ToUpperInvariant())) continue;
                var user = NewUser(email, r.TryGetValue("fullname", out var nv) ? nv.Trim() : fid, AccountPasswordHash(fid), true);
                bool hod = r.TryGetValue("ishod", out var hv) && hv.Equals("true", StringComparison.OrdinalIgnoreCase);
                _db.Users.Add(user);
                _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleIds[RoleNames.Faculty] });
                if (hod) _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleIds[RoleNames.Hod] });
                _db.Faculty.Add(new Faculty
                {
                    FacultyID = fid, FullName = r.TryGetValue("fullname", out var n2) ? n2.Trim() : fid, Email = email, Phone = r.TryGetValue("phone", out var pv) ? pv.Trim() : "", BranchId = b.Id, Department = b.Name,
                    Designation = r.TryGetValue("designation", out var dv2) ? dv2.Trim() : "", IsHOD = hod, IsActive = true, IdentityUserId = user.Id
                });
                existingEmails.Add(email.ToUpperInvariant());
                existingFacultyIds.Add(fid);
                added++;
                if (added % 100 == 0) { await _db.SaveChangesAsync(cancellationToken); _db.ChangeTracker.Clear(); }
            }
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
        }

        // Ensure every faculty has a login and standardized email
        var allFaculty = await _db.Faculty.ToListAsync();
        foreach (var f in allFaculty)
        {
                var expectedEmail = EmailHelper.FacultyEmail(f.FacultyID, f.FullName);
            if (f.Email != expectedEmail)
            {
                f.Email = expectedEmail;
            }

            var u = f.IdentityUserId != null ? await _users.FindByIdAsync(f.IdentityUserId) : await _users.FindByEmailAsync(f.Email);
            if (u == null)
            {
                u = NewUser(f.Email, f.FullName, AccountPasswordHash(f.FacultyID), true);
                await _users.CreateAsync(u, AccountPassword(f.FacultyID));
                await _users.AddToRoleAsync(u, RoleNames.Faculty);
                if (f.IsHOD) await _users.AddToRoleAsync(u, RoleNames.Hod);
            }
            else
            {
                if (u.Email != f.Email)
                {
                    u.Email = f.Email;
                    u.NormalizedEmail = f.Email.ToUpperInvariant();
                    u.UserName = f.Email;
                    u.NormalizedUserName = f.Email.ToUpperInvariant();
                    await _users.UpdateAsync(u);
                }
                if (string.IsNullOrEmpty(u.PasswordHash) || u.MustChangePassword)
                {
                    u.PasswordHash = AccountPasswordHash(f.FacultyID);
                    u.MustChangePassword = true;
                    await _users.UpdateAsync(u);
                }
            }
            f.IdentityUserId = u.Id;
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedStudentsAsync(CancellationToken cancellationToken = default)
    {
        // Pre-compute password hashes in parallel (PBKDF2 is CPU-bound; each student ID yields one hash).
        var rows = Read("students.csv");
        var hashCache = new ConcurrentDictionary<string, string>();
        Parallel.ForEach(rows.Where(r => !string.IsNullOrWhiteSpace(r.TryGetValue("studentid", out var sv) ? sv.Trim() : "")), r =>
        {
            var sid = r["studentid"].Trim();
            hashCache[sid] = AccountPasswordHash(sid);
        });
        string HashFor(string id) => hashCache.TryGetValue(id, out var h) ? h : AccountPasswordHash(id);
        var studentRole = (await _db.Roles.FirstAsync(r => r.Name == RoleNames.Student)).Id;

        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Code);
        var sections = await _db.Sections.AsNoTracking().ToListAsync();
        var existingNormalizedEmails = (await _db.Users.AsNoTracking().Select(u => u.NormalizedEmail!).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        var existingStudentIDs = (await _db.Students.AsNoTracking().Select(s => s.StudentID).ToListAsync()).ToHashSet(StringComparer.Ordinal);

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        if (rows.Count > 0)
        {
            if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeding {Count} students with logins...", rows.Count);
            int added = 0;
            foreach (var r in rows)
            {
                var sid = r.TryGetValue("studentid", out var sv) ? sv.Trim() : "";
                if (string.IsNullOrWhiteSpace(sid)) continue;
                if (existingStudentIDs.Contains(sid)) continue;
                if (!branches.TryGetValue(r["branchcode"], out var b)) continue;
                int sem = int.Parse(r["semester"]);
                string secName = r["section"];
                if (!sections.Any(s => s.BranchId == b.Id && s.Name == secName)) continue;
                var sec = sections.First(s => s.BranchId == b.Id && s.Name == secName);
                var email = EmailHelper.StudentEmail(sid, r.TryGetValue("fullname", out var fn2) ? fn2 : sid);
                string normEmail = email.ToUpperInvariant();
                if (existingNormalizedEmails.Contains(normEmail)) continue;

                    var user = NewUser(email, r["fullname"], HashFor(sid), true);
                    _db.Users.Add(user);
                    _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = studentRole });
                    _db.Students.Add(new Student
                    {
                        StudentID = r["studentid"], FullName = r["fullname"], Email = email, Phone = r["phone"], BranchId = b.Id, SectionId = sec.Id, Semester = sem,
                        Year = YearHelper.FromSemester(sem), JoiningDate = DateTime.SpecifyKind(DateTime.Parse(r["joiningdate"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).Date, DateTimeKind.Utc), QRPath = "on-demand", IsActive = true, IdentityUserId = user.Id
                    });
                existingNormalizedEmails.Add(normEmail);
                existingStudentIDs.Add(sid);
                added++;
                if (added % 200 == 0) { await _db.SaveChangesAsync(cancellationToken); _db.ChangeTracker.Clear(); }
            }
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
        }

        foreach (var sec in sections)
        {
            var b = branches.Values.FirstOrDefault(x => x.Id == sec.BranchId);
            if (b == null) continue;
            int sems = SemestersForBranch(b.Code);
            for (int sem = 1; sem <= sems; sem++)
            {
                var sectionStudents = await _db.Students.Where(s => s.BranchId == b.Id && s.SectionId == sec.Id && s.Semester == sem).ToListAsync();
                int targetSize = ClassSizeFor(b.Id, sec.Id, sem);
                int currentCount = sectionStudents.Count;
                if (currentCount >= targetSize) continue;

                for (int i = currentCount + 1; i <= targetSize; i++)
                {
                    string studentId = StudentIdService.Format(StudentIdService.Prefix(sem, sec.Name, b.Code), i);
                    string studentName = SampleStudentNames[(i + sem * 7) % SampleStudentNames.Length];
                    string email = EmailHelper.StudentEmail(studentId, studentName);
                    string normEmail = email.ToUpperInvariant();
                    if (existingNormalizedEmails.Contains(normEmail) || existingStudentIDs.Contains(studentId)) continue;

                    var user = NewUser(email, studentName, HashFor(studentId), true);
                    _db.Users.Add(user);
                    _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = studentRole });
                    _db.Students.Add(new Student
                    {
                        StudentID = studentId,
                        FullName = studentName,
                        Email = email,
                        Phone = "90000" + i.ToString("D5"),
                        BranchId = b.Id,
                        SectionId = sec.Id,
                        Semester = sem,
                        Year = YearHelper.FromSemester(sem),
                        JoiningDate = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc).AddMonths(-6),
                        QRPath = "on-demand",
                        IsActive = true,
                        IdentityUserId = user.Id
                    });
                    existingNormalizedEmails.Add(normEmail);
                    existingStudentIDs.Add(studentId);
                }
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
            }
        }

        // Ensure every student has a login and standardized email
        var allStudents = await _db.Students.ToListAsync();
        foreach (var s in allStudents)
        {
            var expectedEmail = EmailHelper.StudentEmail(s.StudentID, s.FullName);
            if (s.Email != expectedEmail)
            {
                s.Email = expectedEmail;
            }

            var u = s.IdentityUserId != null ? await _users.FindByIdAsync(s.IdentityUserId) : await _users.FindByEmailAsync(s.Email);
            if (u == null)
            {
                u = NewUser(s.Email, s.FullName, HashFor(s.StudentID), true);
                await _users.CreateAsync(u, AccountPassword(s.StudentID));
                await _users.AddToRoleAsync(u, RoleNames.Student);
            }
            else
            {
                if (u.Email != s.Email)
                {
                    u.Email = s.Email;
                    u.NormalizedEmail = s.Email.ToUpperInvariant();
                    u.UserName = s.Email;
                    u.NormalizedUserName = s.Email.ToUpperInvariant();
                    await _users.UpdateAsync(u);
                }
                if (string.IsNullOrEmpty(u.PasswordHash) || u.MustChangePassword)
                {
                    u.PasswordHash = HashFor(s.StudentID);
                    u.MustChangePassword = true;
                    await _users.UpdateAsync(u);
                }
            }
            s.IdentityUserId = u.Id;
        }
        await _db.SaveChangesAsync();

        _db.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    // ------------------------------------------------------------------ timetable + mappings from timetable.csv
    private async Task SeedTimetableAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.Timetables.AnyAsync()) return;
        var rows = Read("timetable.csv");
        if (rows.Count == 0) return;
        var year = await _settings.GetCurrentYearAsync();
        var periods = await _db.PeriodDefinitions.AsNoTracking().ToDictionaryAsync(p => p.PeriodNo);
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Code);
        var sections = (await _db.Sections.AsNoTracking().ToListAsync()).ToDictionary(s => (s.BranchId, s.Name));
        var subjects = await _db.Subjects.AsNoTracking().ToDictionaryAsync(s => s.SubjectCode);
        var faculty = await _db.Faculty.AsNoTracking().ToDictionaryAsync(f => f.FacultyID);

        var mapped = new HashSet<(int, int, int)>();
        var scheduledSlots = new HashSet<(int SectionId, DayOfWeek Day, int Period)>();
        var facultySlots = new HashSet<(int FacultyId, DayOfWeek Day, int Period)>();
        var classDailyCounts = new Dictionary<(int SectionId, DayOfWeek Day), int>();
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        int n = 0;
        var rngTT = new Random(2026);
        foreach (var r in rows)
        {
            if (!branches.TryGetValue(r["branchcode"], out var b)) continue;
            int sem = int.Parse(r["semester"]);
            string secName = r["section"];
            if (!sections.TryGetValue((b.Id, secName), out var sec)
                || !subjects.TryGetValue(r["subjectcode"], out var sub) || !faculty.TryGetValue(r["facultyid"], out var f)) continue;
            int period = int.Parse(r["period"]);
            if (!periods.TryGetValue(period, out var pd)) continue;
            DayOfWeek day = (DayOfWeek)int.Parse(r["day"]);

            if (!scheduledSlots.Add((sec.Id, day, period))) continue;
            if (!facultySlots.Add((f.Id, day, period))) continue;
            // Enforce 5-6 classes per day per class: cap at 6
            var key = (sec.Id, day);
            int count = classDailyCounts.GetValueOrDefault(key, 0);
            if (count >= 6) continue;
            classDailyCounts[key] = count + 1;

            string room = r["room"];
            string classType = r.TryGetValue("classtype", out var ct) && !string.IsNullOrWhiteSpace(ct) ? ct : (room.Contains("Lab", StringComparison.OrdinalIgnoreCase) ? "Lab" : room.Contains("Workshop", StringComparison.OrdinalIgnoreCase) ? "Workshop" : "Lecture");

            _db.Timetables.Add(new Timetable
            {
                BranchId = b.Id, SectionId = sec.Id, Semester = sem, Day = day, Period = period, SubjectId = sub.Id, FacultyId = f.Id,
                RoomNo = room, ClassType = classType, StartTime = pd.StartTime, EndTime = pd.EndTime, AcademicYearId = year.Id
            });
            if (mapped.Add((f.Id, sub.Id, sec.Id)))
                _db.FacultySubjectMappings.Add(new FacultySubjectMapping { FacultyId = f.Id, SubjectId = sub.Id, BranchId = b.Id, SectionId = sec.Id, Semester = sem, AcademicYearId = year.Id });
            if (++n % 1000 == 0) { await _db.SaveChangesAsync(); _db.ChangeTracker.Clear(); }
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _db.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    private async Task SeedNoticesAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.Notices.AnyAsync()) return;
        var now = DateTime.UtcNow;
        _db.Notices.AddRange(
            new Notice { Title = "Welcome to AttendX", Body = "Attendance is now digital. Faculty mark attendance in the period window; students can follow their attendance live on the student dashboard.", Audience = "All", IsPinned = true, PublishedAt = now.AddDays(-3), CreatedBy = "System Administrator" },
            new Notice { Title = "75% attendance is required", Body = "Students below 75% attendance may not be allowed to sit the end-semester exams. Check your dashboard regularly and raise a query if a record looks wrong.", Audience = "Student", PublishedAt = now.AddDays(-2), CreatedBy = "System Administrator" },
            new Notice { Title = "Mark attendance inside the period window", Body = "Attendance can be marked from 10 minutes before the period starts until 10 minutes after it ends. After that only Admin/HOD can correct it, with a reason.", Audience = "Faculty", PublishedAt = now.AddDays(-2), CreatedBy = "System Administrator" });
        await _db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ attendance history (SqlBulkCopy)
    private async Task SeedAttendanceHistoryAsync(CancellationToken cancellationToken = default)
    {
        var tts = await _db.Timetables.AsNoTracking().ToListAsync();
        var students = await _db.Students.AsNoTracking().Select(s => new { s.Id, s.BranchId, s.SectionId, s.Semester }).ToListAsync();
        if (tts.Count == 0 || students.Count == 0) return;

        var currentYear = await _settings.GetCurrentYearAsync();
        var startDate = currentYear?.StartDate ?? new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeding attendance from {Start} to {Today} (real timetable mapping)...", startDate.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"));

        var rnd = new Random(2026);
        var holidays = await new CalendarService(_db, new SystemClock()).HolidayDatesAsync(startDate, today);
        // Exclude exam periods where no regular classes are held
        var examDates = await _db.ExamTimetables.AsNoTracking()
            .Where(e => e.ExamDate >= startDate && e.ExamDate <= today)
            .Select(e => DateTime.SpecifyKind(e.ExamDate.Date, DateTimeKind.Utc))
            .Distinct()
            .ToListAsync();
        var excludeDates = holidays.Union(examDates).ToHashSet();

        var byClass = students.GroupBy(s => (s.BranchId, s.SectionId, s.Semester)).ToDictionary(g => g.Key, g => g.ToList());
        var prop = students.ToDictionary(s => s.Id, s => { var r = rnd.NextDouble(); return r < 0.2 ? 0.55 + rnd.NextDouble() * 0.17 : r < 0.8 ? 0.78 + rnd.NextDouble() * 0.12 : 0.92 + rnd.NextDouble() * 0.06; });
        var decliners = students.Where(_ => rnd.NextDouble() < 0.08).Select(s => s.Id).ToHashSet();

        var totalDays = (int)(today - startDate).TotalDays;
        var jobs = Enumerable.Range(0, totalDays).Select(i => startDate.AddDays(i))
            .Where(d => d.DayOfWeek != DayOfWeek.Sunday && !excludeDates.Contains(d))
            .SelectMany(d => tts.Where(t => t.Day == d.DayOfWeek).Select(t => (Date: d, T: t)))
            .ToList();

        await _db.Database.OpenConnectionAsync();
        try
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = false;
            foreach (var chunk in jobs.Chunk(1000))
            {
                var sessions = chunk.Select(j => new AttendanceSession
                {
                    TimetableId = j.T.Id, AcademicYearId = j.T.AcademicYearId, Date = j.Date, Period = j.T.Period, SubjectId = j.T.SubjectId, SectionId = j.T.SectionId,
                    FacultyId = j.T.FacultyId, Mode = "Manual", DeviceId = "seed", StartedAt = j.Date + j.T.StartTime
                }).ToList();
                _db.AttendanceSessions.AddRange(sessions);
                await _db.SaveChangesAsync();

                var table = new DataTable();
                table.Columns.Add("StudentId", typeof(int)); table.Columns.Add("SubjectId", typeof(int)); table.Columns.Add("FacultyId", typeof(int));
                table.Columns.Add("SectionId", typeof(int)); table.Columns.Add("BranchId", typeof(int)); table.Columns.Add("Semester", typeof(int));
                table.Columns.Add("Date", typeof(DateTime)); table.Columns.Add("Period", typeof(int)); table.Columns.Add("Status", typeof(string));
                table.Columns.Add("MarkedAt", typeof(DateTime)); table.Columns.Add("AttendanceSessionId", typeof(int)); table.Columns.Add("Source", typeof(string)); table.Columns.Add("DeviceId", typeof(string));

                for (int i = 0; i < chunk.Length; i++)
                {
                    var (date, t) = chunk[i];
                    if (!byClass.TryGetValue((t.BranchId, t.SectionId, t.Semester), out var list)) continue;
                    foreach (var s in list)
                    {
                        double p = prop[s.Id];
                        if (decliners.Contains(s.Id) && date >= today.AddDays(-14)) p *= 0.55;
                        var status = rnd.NextDouble() < p ? (rnd.NextDouble() < 0.06 ? "Late" : "Present") : "Absent";
                        table.Rows.Add(s.Id, t.SubjectId, t.FacultyId, t.SectionId, s.BranchId, s.Semester, date, t.Period, status, date + t.StartTime.Add(TimeSpan.FromMinutes(5)), sessions[i].Id, "Manual", "seed");
                    }
                }

                if (_usePostgres)
                {
                    var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
                    using var writer = conn.BeginBinaryImport("COPY \"Attendance\" (\"StudentId\", \"SubjectId\", \"FacultyId\", \"SectionId\", \"BranchId\", \"Semester\", \"Date\", \"Period\", \"Status\", \"MarkedAt\", \"AttendanceSessionId\", \"Source\", \"DeviceId\") FROM STDIN (FORMAT BINARY)");
                    foreach (DataRow row in table.Rows)
                    {
                        writer.StartRow();
                        writer.Write(row.Field<int>("StudentId"), NpgsqlTypes.NpgsqlDbType.Integer);
                        writer.Write(row.Field<int>("SubjectId"), NpgsqlTypes.NpgsqlDbType.Integer);
                        writer.Write(row.Field<int>("FacultyId"), NpgsqlTypes.NpgsqlDbType.Integer);
                        writer.Write(row.Field<int>("SectionId"), NpgsqlTypes.NpgsqlDbType.Integer);
                        writer.Write(row.Field<int>("BranchId"), NpgsqlTypes.NpgsqlDbType.Integer);
                        writer.Write(row.Field<int>("Semester"), NpgsqlTypes.NpgsqlDbType.Integer);
                        writer.Write(row.Field<DateTime>("Date"), NpgsqlTypes.NpgsqlDbType.Timestamp);
                        writer.Write(row.Field<int>("Period"), NpgsqlTypes.NpgsqlDbType.Integer);
                        writer.Write(row.Field<string>("Status")!, NpgsqlTypes.NpgsqlDbType.Varchar);
                        writer.Write(row.Field<DateTime>("MarkedAt"), NpgsqlTypes.NpgsqlDbType.Timestamp);
                        writer.Write(row.Field<int>("AttendanceSessionId"), NpgsqlTypes.NpgsqlDbType.Integer);
                        writer.Write(row.Field<string>("Source")!, NpgsqlTypes.NpgsqlDbType.Varchar);
                        writer.Write(row.Field<string>("DeviceId")!, NpgsqlTypes.NpgsqlDbType.Varchar);
                    }
                    writer.Complete();
                }
                else
                {
                    var conn = (SqlConnection)_db.Database.GetDbConnection();
                    using var bulk = new SqlBulkCopy(conn) { DestinationTableName = "dbo.Attendance", BatchSize = 10000, BulkCopyTimeout = 600 };
                    foreach (DataColumn c in table.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
                    await bulk.WriteToServerAsync(table);
                }
                _db.ChangeTracker.Clear();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Attendance history seeding failed - the app still works, you just have no sample history.");
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
            await _db.Database.CloseConnectionAsync();
        }
    }

    private async Task FixMalformedStudentIdsAsync(CancellationToken cancellationToken = default)
    {
        var students = await _db.Students.ToListAsync();
        bool changed = false;
        int idx = 0;
        foreach (var s in students)
        {
            if (!string.IsNullOrEmpty(s.StudentID))
            {
                var cleaned = System.Text.RegularExpressions.Regex.Replace(s.StudentID, @"^([1-8])\1+", "$1");
                if (cleaned != s.StudentID)
                {
                    s.StudentID = cleaned;
                    changed = true;
                }
            }
            if (!string.IsNullOrEmpty(s.FullName) && s.FullName.StartsWith("Student "))
            {
                s.FullName = SampleStudentNames[idx % SampleStudentNames.Length];
                changed = true;
            }
            string cleanName = (s.FullName ?? "student").Trim().ToLowerInvariant().Replace(" ", ".");
            string expectedEmail = EmailHelper.StudentEmail(s.StudentID, s.FullName);
            if (s.Email != expectedEmail)
            {
                s.Email = expectedEmail;
                changed = true;
            }
            idx++;
        }
        if (changed) await _db.SaveChangesAsync();
    }

    private async Task SeedExamTimetableAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.ExamTimetables.AnyAsync(cancellationToken)) return;

        var academics = await _db.AcademicYears.ToListAsync(cancellationToken);
        // Ensure 2 academic years exist
        if (academics.Count < 2)
        {
            if (!academics.Any(a => a.Name == "2026-27"))
            {
                academics.Add(new AcademicYear { Name = "2026-27", StartDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), EndDate = new DateTime(2027, 5, 31, 0, 0, 0, DateTimeKind.Utc), IsCurrent = true });
            }
            if (!academics.Any(a => a.Name == "2027-28"))
            {
                academics.Add(new AcademicYear { Name = "2027-28", StartDate = new DateTime(2027, 8, 1, 0, 0, 0, DateTimeKind.Utc), EndDate = new DateTime(2028, 5, 31, 0, 0, 0, DateTimeKind.Utc), IsCurrent = false });
            }
            await _db.SaveChangesAsync(cancellationToken);
            academics = await _db.AcademicYears.ToListAsync(cancellationToken);
        }

        var subjects = await _db.Subjects.AsNoTracking().Where(s => s.IsActive).ToListAsync(cancellationToken);
        var mappings = await _db.FacultySubjectMappings.AsNoTracking().ToListAsync(cancellationToken);
        var periods = await _db.PeriodDefinitions.AsNoTracking().OrderBy(p => p.PeriodNo).ToListAsync(cancellationToken);
        var defaultStart = periods.FirstOrDefault()?.StartTime ?? new TimeSpan(9, 0, 0);
        var defaultEnd = periods.FirstOrDefault()?.EndTime ?? new TimeSpan(9, 55, 0);

        // Build faculty lookup: (BranchId, Semester, SubjectId) -> FacultyId
        var facMap = mappings.GroupBy(m => (m.BranchId, m.Semester, m.SubjectId))
            .ToDictionary(g => g.Key, g => g.First().FacultyId);

        var exams = new List<ExamTimetable>();
        var rnd = new Random(2026);
        foreach (var year in academics)
        {
            int startY = int.Parse(year.Name.Split('-')[0]);
            // Odd sem (running now): Mid1 Sep (completed), Mid2 3rd week Nov, Final end Dec
            // Even sem: Mid1 Mar, Mid2 Apr, Final May + summer break Jun-Jul
            var windowsForYear = new List<(DateTime Start, DateTime End, string Type)>
            {
                (new DateTime(startY, 9, 22, 0, 0, 0, DateTimeKind.Utc), new DateTime(startY, 9, 27, 0, 0, 0, DateTimeKind.Utc), "Mid1"),
                (new DateTime(startY, 11, 17, 0, 0, 0, DateTimeKind.Utc), new DateTime(startY, 11, 22, 0, 0, 0, DateTimeKind.Utc), "Mid2"),
                (new DateTime(startY, 12, 21, 0, 0, 0, DateTimeKind.Utc), new DateTime(startY + 1, 1, 5, 0, 0, 0, DateTimeKind.Utc), "Final"),
                // Even sem next calendar year
                (new DateTime(startY + 1, 3, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(startY + 1, 3, 16, 0, 0, 0, DateTimeKind.Utc), "Mid1"),
                (new DateTime(startY + 1, 4, 8, 0, 0, 0, DateTimeKind.Utc), new DateTime(startY + 1, 4, 14, 0, 0, 0, DateTimeKind.Utc), "Mid2"),
                (new DateTime(startY + 1, 5, 5, 0, 0, 0, DateTimeKind.Utc), new DateTime(startY + 1, 5, 20, 0, 0, 0, DateTimeKind.Utc), "Final")
            };

            foreach (var subj in subjects)
            {
                int facId = facMap.TryGetValue((subj.BranchId, subj.Semester, subj.Id), out int f) ? f : 0;
                if (facId == 0) continue;

                foreach (var w in windowsForYear)
                {
                    // Pick a weekday in window (Mon-Sat) based on subject hash for determinism
                    int dayOffset = (subj.Id + subj.BranchId) % ((int)(w.End - w.Start).TotalDays + 1);
                    var date = w.Start.AddDays(dayOffset);
                    while (date.DayOfWeek == DayOfWeek.Sunday) date = date.AddDays(1);
                    if (date > w.End) date = w.End;

                    var roomPool = new[] { "Exam Hall A", "Exam Hall B", "Exam Hall C", "Exam Hall D", "Exam Hall E" };
                    int roomIdx = (subj.BranchId * 3 + subj.Id + date.DayOfYear) % roomPool.Length;
                    var room = roomPool[roomIdx];
                    var startTime = (roomIdx % 2 == 0) ? defaultStart : new TimeSpan(13, 0, 0);
                    var endTime = startTime.Add(TimeSpan.FromHours(3));

                    exams.Add(new ExamTimetable
                    {
                        AcademicYearId = year.Id,
                        BranchId = subj.BranchId,
                        Semester = subj.Semester,
                        SubjectId = subj.Id,
                        FacultyId = facId,
                        ExamType = w.Type,
                        ExamDate = date,
                        StartTime = startTime,
                        EndTime = endTime,
                        RoomNo = room,
                        IsActive = true
                    });
                }
            }
        }

        _db.ExamTimetables.AddRange(exams);
        await _db.SaveChangesAsync(cancellationToken);
        if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeded {Count} exam timetable entries.", exams.Count);

        await SeedExamMarksAsync(cancellationToken);
    }

    private async Task SeedExamMarksAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.ExamMarks.AnyAsync(cancellationToken)) return;

        var exams = await _db.ExamTimetables.AsNoTracking().ToListAsync(cancellationToken);
        var students = await _db.Students.AsNoTracking().ToListAsync(cancellationToken);
        if (exams.Count == 0 || students.Count == 0) return;

        var rnd = new Random(2026);
        var byClass = students.GroupBy(s => (s.BranchId, s.Semester)).ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList());

        var marksToInsert = new List<ExamMark>();
        foreach (var exam in exams)
        {
            if (!byClass.TryGetValue((exam.BranchId, exam.Semester), out var sIds)) continue;

            foreach (var sId in sIds)
            {
                bool isMid1 = exam.ExamType == "Mid1";
                bool isMid2 = exam.ExamType == "Mid2";
                // Mid1: 100% locked and finalized; Mid2: 85% locked; Final: locked but not graded yet
                var isLocked = isMid1 ? true : (isMid2 ? rnd.NextDouble() < 0.85 : true);
                
                // Mid1 marks are already corrected and finalized (55-95). Mid2 marks draft (50-85). Final marks generated later.
                var mObtained = (decimal)Math.Round(isMid1 ? (55 + (rnd.NextDouble() * 40)) : (50 + (rnd.NextDouble() * 35)), 2);
                if (mObtained > 95) mObtained = 95;

                var roll = rnd.NextDouble();
                if (roll < 0.08) mObtained = (decimal)Math.Round(15 + (rnd.NextDouble() * 20), 2);
                else if (roll > 0.92) mObtained = (decimal)Math.Round(96 + (rnd.NextDouble() * 4), 2);

                marksToInsert.Add(new ExamMark
                {
                    ExamTimetableId = exam.Id,
                    StudentId = sId,
                    MarksObtained = mObtained,
                    MaxMarks = 100,
                    IsLocked = isLocked,
                    Remarks = isLocked ? "Locked" : "Draft",
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedBy = isLocked ? "HOD" : "Faculty",
                    LastModifiedAt = DateTime.UtcNow.AddDays(-rnd.NextDouble() * 10)
                });
            }
        }

        // Bulk insert in chunks to prevent memory issues
        foreach (var chunk in marksToInsert.Chunk(1000))
        {
            _db.ExamMarks.AddRange(chunk);
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
        }

        if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeded {Count} exam marks for students.", marksToInsert.Count);
    }

    // ------------------------------------------------------------------ self check (logged; also shown on the Health page)
    private async Task SelfCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var perClass = await _db.Students.Where(s => s.IsActive).GroupBy(s => new { s.BranchId, s.SectionId, s.Semester }).Select(g => g.Count()).ToListAsync();
            int bad = perClass.Count(c => c != 30);
            int faculty = await _db.Faculty.CountAsync(), students = await _db.Students.CountAsync(), noLogin = await _db.Students.CountAsync(s => s.IdentityUserId == null);
            if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Self-check: {Classes} classes, {Students} students, {Faculty} faculty, {Bad} classes without exactly 30 students, {NoLogin} students without login", perClass.Count, students, faculty, bad, noLogin);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Self-check failed"); }
    }
}
