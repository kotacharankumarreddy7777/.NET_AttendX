using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private static readonly Dictionary<string, (string Prefix, int Count)> SectionRules = new()
    {
        // Each branch runs 2 sections (EV1..EV2 / EN1..EN2 etc.) section per semester — matches the dataset.
        // Sections are reused across all 8 semesters of a 4-year B.Tech program.
        ["CSE"] = ("EV", 2), ["CSEAI"] = ("EN", 2), ["CSEAIML"] = ("EN", 2), ["ADS"] = ("EU", 2), ["ECE"] = ("EC", 2), ["EEE"] = ("EE", 2),
        ["MECH"] = ("ME", 2), ["CIVIL"] = ("CE", 2), ["IT"] = ("IT", 2), ["CYBER"] = ("CS", 2), ["AIDS"] = ("AD", 2), ["AIML"] = ("AM", 2), ["IOT"] = ("IO", 2), ["DS"] = ("DS", 2)
    };

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

    private string Csv(string name) => Path.Combine(_env.ContentRootPath, "Dataset", name);
    private List<Dictionary<string, string>> Read(string name) => File.Exists(Csv(name)) ? CsvParser.ParseWithHeader(File.ReadAllText(Csv(name))) : [];

    public async Task SeedAsync()
    {
        if (_o.UseMigrations) await _db.Database.MigrateAsync();
        else await EnsureDatabaseSchemaAsync();

        foreach (var r in RoleNames.All)
            if (!await _roles.RoleExistsAsync(r)) await _roles.CreateAsync(new IdentityRole(r));

        await _settings.GetCurrentYearAsync();
        await _settings.GetAsync();
        await SeedPeriodsAsync();
        await SeedBranchesAndSectionsAsync();
        await SeedAdminAsync();
        await SeedSubjectsAsync();
        await SeedSemestersAsync();
        await SeedAcademicCalendarAsync();
        await FixMalformedStudentIdsAsync();
        if (!_o.SeedDemoData) return;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await SeedHolidaysAsync();
        await SeedLeaveTypesAsync();
        await SeedFacultyAsync();
        await SeedStudentsAsync();
        await SeedParentsAsync();
        await SeedLeaveBalancesAsync();
        await SeedTimetableAsync();
        await SeedNoticesAsync();
        await SeedExamsAsync();
        await SeedExamTimetableAsync();
        await SeedExamRegistrationsAsync();
        await SeedExamMarksAsync();
        await SeedAttendanceHistoryAsync();
        await SelfCheckAsync();
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

    /// <summary>
    /// EnsureCreated only builds the schema when the database does not exist at all, so a database created by an older
    /// model (renamed or missing tables/columns) would never be fixed. Every table is rebuilt from the Dataset files,
    /// so when the stored schema does not match the current model the database is dropped and created again.
    /// </summary>
    private async Task EnsureDatabaseSchemaAsync()
    {
        await _db.Database.EnsureCreatedAsync();
        var missing = await MissingSchemaObjectsAsync();
        if (missing.Count == 0) return;

        if (!_o.RecreateIfSchemaMismatch)
            throw new InvalidOperationException(
                $"The database schema does not match the current model (missing: {string.Join(", ", missing.Take(10))}). " +
                "Set AttendX:RecreateIfSchemaMismatch to true to rebuild it, or migrate the database manually.");

        _log.LogWarning("Schema is out of date ({Count} missing objects: {Missing}). Dropping and recreating the database; all data is re-seeded from the Dataset folder.",
            missing.Count, string.Join(", ", missing.Take(10)));
        await _db.Database.EnsureDeletedAsync();
        await _db.Database.EnsureCreatedAsync();
    }

    private async Task<List<string>> MissingSchemaObjectsAsync()
    {
        var expected = _db.Model.GetEntityTypes()
            .SelectMany(e =>
            {
                var table = e.GetTableName();
                return table == null ? [] : e.GetProperties().Select(p => $"{table}.{p.GetColumnName()}");
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var actual = (await _db.Database.SqlQueryRaw<string>(
                @"SELECT table_name || '.' || column_name AS [Value]
                  FROM information_schema.columns
                  WHERE table_schema = 'public'")
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Detect both missing columns (expected but not actual) and extra columns (actual but not expected)
        var missing = expected.Except(actual, StringComparer.OrdinalIgnoreCase);
        var extra = actual.Except(expected, StringComparer.OrdinalIgnoreCase);
        return [.. missing.Union(extra).OrderBy(x => x)];
    }

    private async Task SeedPeriodsAsync()
    {
        if (await _db.PeriodDefinitions.AnyAsync()) return;
        // Real-world Indian B.Tech timetable: Lecture = 60 min, Lab = 80 min (1 h 20 min), 7 periods a day.
        // 09:00-10:00 (L) · 10:00-11:00 (L) · 11:00-12:00 (L) · 12:00-13:00 (L) · break · 14:00-15:20 (Lab/L) · 15:20-16:20 (L) · 16:20-17:20 (L)
        var slots = new (int No, string Start, string End)[]
        {
            (1, "09:00", "10:00"),
            (2, "10:00", "11:00"),
            (3, "11:00", "12:00"),
            (4, "12:00", "13:00"),
            (5, "14:00", "15:20"),   // 80 minutes (lab block if needed)
            (6, "15:20", "16:20"),
            (7, "16:20", "17:20")
        };
        for (int i = 0; i < Math.Min(_o.PeriodCount, slots.Length); i++)
        {
            var s = TimeSpan.Parse(slots[i].Start);
            var e = TimeSpan.Parse(slots[i].End);
            _db.PeriodDefinitions.Add(new PeriodDefinition { PeriodNo = slots[i].No, StartTime = s, EndTime = e });
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedBranchesAndSectionsAsync()
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
            for (int sem = 1; sem <= 8; sem++)
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

    private async Task SeedAdminAsync()
    {
        if (await _users.FindByEmailAsync(_o.AdminEmail) != null) return;
        var admin = new ApplicationUser { UserName = _o.AdminEmail, Email = _o.AdminEmail, EmailConfirmed = true, FullName = "System Administrator", MustChangePassword = _o.ForceAdminPasswordChange, LockoutEnabled = true };
        var res = await _users.CreateAsync(admin, _o.AdminPassword);
        if (res.Succeeded) await _users.AddToRoleAsync(admin, RoleNames.Admin);
        else _log.LogError("Could not create admin: {Errors}", string.Join("; ", res.Errors.Select(e => e.Description)));
    }

    private async Task SeedSubjectsAsync()
    {
        var rows = Read("subjects.csv");
        if (rows.Count == 0) return;
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Code);
        var codes = (await _db.Subjects.Select(s => s.SubjectCode).ToListAsync()).ToHashSet();
        foreach (var r in rows)
        {
            if (!branches.TryGetValue(r["branchcode"], out var b) || codes.Contains(r["subjectcode"])) continue;
            _db.Subjects.Add(new Subject { BranchId = b.Id, Semester = int.Parse(r["semester"]), SubjectCode = r["subjectcode"], SubjectName = r["subjectname"], Credits = int.Parse(r["credits"]) });
            codes.Add(r["subjectcode"]);
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedHolidaysAsync()
    {
        if (await _db.Holidays.AnyAsync()) return;
        foreach (var r in Read("holidays.csv"))
            _db.Holidays.Add(new Holiday { Date = DateTime.Parse(r["date"]).Date, Name = r["name"], Type = r["type"] });

        var year = await _settings.GetCurrentYearAsync();
        int start = int.TryParse(year.Name.Split('-')[0], out var sy) ? sy : DateTime.Today.Year;

        // Odd-sem end-of-semester 1-week break (Dec 31 - Jan 9)
        _db.Holidays.Add(new Holiday { Date = new DateTime(start, 12, 31), Name = "Odd Semester Break - Day 1", Type = "Holiday" });
        for (int d = 1; d <= 9; d++)
            _db.Holidays.Add(new Holiday { Date = new DateTime(start + 1, 1, d), Name = "Odd Semester Break", Type = "Holiday" });

        // Summer vacation (Jun 1 - Jul 31)
        for (int d = 1; d <= 30; d++) _db.Holidays.Add(new Holiday { Date = new DateTime(start + 1, 6, d), Name = "Summer Vacation", Type = "Holiday" });
        for (int d = 1; d <= 31; d++) _db.Holidays.Add(new Holiday { Date = new DateTime(start + 1, 7, d), Name = "Summer Vacation", Type = "Holiday" });

        // National + festival holidays for the academic year
        var std = new (int y, int m, int d, string n)[]
        {
            (start, 8, 15, "Independence Day"),
            (start, 10, 2, "Gandhi Jayanti"),
            (start, 11, 1, "Diwali / Kannada Rajyotsava"),
            (start, 11, 9, "Guru Nanak Jayanti"),
            (start, 12, 25, "Christmas"),
            (start + 1, 1, 14, "Uttarayan (Makar Sankranti)"),
            (start + 1, 1, 26, "Republic Day"),
            (start + 1, 3, 8, "Holi"),
            (start + 1, 4, 14, "Dr. Ambedkar Jayanti"),
            (start + 1, 5, 1, "May Day"),
        };
        foreach (var (y, m, d, n) in std)
        {
            var dt = new DateTime(y, m, d);
            if (!await _db.Holidays.AnyAsync(h => h.Date == dt))
                _db.Holidays.Add(new Holiday { Date = dt, Name = n, Type = "Holiday" });
        }

        await _db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ people (fast path)
    private static ApplicationUser NewUser(string email, string fullName, string hash) => new()
    {
        Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true, PasswordHash = hash, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(),
        LockoutEnabled = true, FullName = fullName, MustChangePassword = false
    };

    private async Task SeedFacultyAsync()
    {
        var rows = Read("faculty.csv");
        var hash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), _o.SeedFacultyPassword);
        var roleIds = await _db.Roles.ToDictionaryAsync(r => r.Name!, r => r.Id);
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Code);
        var existingEmails = (await _db.Users.Select(u => u.NormalizedEmail).ToListAsync()).ToHashSet();

        if (rows.Count > 0 && !await _db.Faculty.AnyAsync())
        {
            if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeding {Count} faculty with logins...", rows.Count);
            foreach (var r in rows)
            {
                var email = r["email"].Trim().ToLowerInvariant();
                if (existingEmails.Contains(email.ToUpperInvariant()) || !branches.TryGetValue(r["department"], out var b)) continue;
                var user = NewUser(email, r["fullname"], hash);
                bool hod = r["ishod"].Equals("true", StringComparison.OrdinalIgnoreCase);
                _db.Users.Add(user);
                _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleIds[RoleNames.Faculty] });
                if (hod) _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleIds[RoleNames.Hod] });
                _db.Faculty.Add(new Faculty
                {
                    FacultyID = r["facultyid"], FullName = r["fullname"], Email = email, Phone = r["phone"], BranchId = b.Id, Department = b.Name,
                    Designation = r["designation"], IsHOD = hod, IsActive = true, IdentityUserId = user.Id
                });
                existingEmails.Add(email.ToUpperInvariant());
            }
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();
        }

        // Ensure every faculty has a login
        var allFaculty = await _db.Faculty.ToListAsync();
        foreach (var f in allFaculty)
        {
            var u = f.IdentityUserId != null ? await _users.FindByIdAsync(f.IdentityUserId) : await _users.FindByEmailAsync(f.Email);
            if (u == null)
            {
                u = NewUser(f.Email, f.FullName, hash);
                await _users.CreateAsync(u, _o.SeedFacultyPassword);
                await _users.AddToRoleAsync(u, RoleNames.Faculty);
                if (f.IsHOD) await _users.AddToRoleAsync(u, RoleNames.Hod);
            }
            else
            {
                if (string.IsNullOrEmpty(u.PasswordHash))
                {
                    u.PasswordHash = hash;
                    await _users.UpdateAsync(u);
                }
            }
            f.IdentityUserId = u.Id;
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedStudentsAsync()
    {
        var rows = Read("students.csv");
        var hash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), _o.SeedStudentPassword);
        var studentRole = (await _db.Roles.FirstAsync(r => r.Name == RoleNames.Student)).Id;
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Code);
        var sections = await _db.Sections.AsNoTracking().ToListAsync();
        var existingNormalizedEmails = (await _db.Users.AsNoTracking().Select(u => u.NormalizedEmail!).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        var existingStudentIDs = (await _db.Students.AsNoTracking().Select(s => s.StudentID).ToListAsync()).ToHashSet(StringComparer.Ordinal);

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        if (rows.Count > 0 && !await _db.Students.AnyAsync())
        {
            if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeding {Count} students with logins...", rows.Count);
            foreach (var chunk in rows.Chunk(500))
            {
                foreach (var r in chunk)
                {
                    if (!branches.TryGetValue(r["branchcode"], out var b)) continue;
                    int sem = int.Parse(r["semester"]);
                    if (sem % 2 == 0) continue;
                    string secName = r["section"];
                    if (!sections.Any(s => s.BranchId == b.Id && s.Name == secName)) continue;
                    var sec = sections.First(s => s.BranchId == b.Id && s.Name == secName);
                    var email = r["email"].Trim();
                    string normEmail = email.ToUpperInvariant();
                    if (existingNormalizedEmails.Contains(normEmail)) continue;
                    existingNormalizedEmails.Add(normEmail);

                    var user = NewUser(email, r["fullname"], hash);
                    _db.Users.Add(user);
                    _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = studentRole });
                    _db.Students.Add(new Student
                    {
                        StudentID = r["studentid"], FullName = r["fullname"], Email = email, Phone = r["phone"], BranchId = b.Id, SectionId = sec.Id, Semester = sem,
                        Year = YearHelper.FromSemester(sem), JoiningDate = DateTime.Parse(r["joiningdate"]).Date, QRPath = "on-demand", IsActive = true, IdentityUserId = user.Id
                    });
                }
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
            }
        }

        foreach (var sec in sections)
        {
            var b = branches.Values.FirstOrDefault(x => x.Id == sec.BranchId);
            if (b == null) continue;
            foreach (int sem in new[] { 1, 3, 5, 7 })
            {
                var sectionStudents = await _db.Students.Where(s => s.BranchId == b.Id && s.SectionId == sec.Id && s.Semester == sem).ToListAsync();
                int currentCount = sectionStudents.Count;
                if (currentCount >= 30) continue;

                for (int i = currentCount + 1; i <= 30; i++)
                {
                    string studentId = StudentIdService.Format(StudentIdService.Prefix(sem, sec.Name, b.Code), i);
                    string studentName = SampleStudentNames[(i + sem * 7) % SampleStudentNames.Length];
                    string cleanName = studentName.Trim().ToLowerInvariant().Replace(" ", ".");
                    string email = $"{cleanName}.{studentId}@attendx.edu.in";
                    string normEmail = email.ToUpperInvariant();
                    if (existingNormalizedEmails.Contains(normEmail) || existingStudentIDs.Contains(studentId)) continue;
                    existingNormalizedEmails.Add(normEmail);
                    existingStudentIDs.Add(studentId);

                    var user = NewUser(email, studentName, hash);
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
                        JoiningDate = DateTime.Today.AddMonths(-6),
                        QRPath = "on-demand",
                        IsActive = true,
                        IdentityUserId = user.Id
                    });
                }
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
            }
        }

        // Ensure every student has a login (fast path: only create users for students missing one)
        var missingLogin = await _db.Students.AsNoTracking().Where(s => s.IdentityUserId == null).Select(s => new { s.Id, s.Email, s.FullName }).ToListAsync();
        if (missingLogin.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in missingLogin)
            {
                var userId = Guid.NewGuid().ToString();
                var normEmail = s.Email.ToUpperInvariant();
                var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                sb.Append($"INSERT INTO public.\"AspNetUsers\" (\"Id\", \"FullName\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PasswordHash\", \"SecurityStamp\", \"ConcurrencyStamp\", \"PhoneNumber\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnd\", \"LockoutEnabled\", \"AccessFailedCount\", \"MustChangePassword\", \"CreatedAt\") VALUES ('{userId}','{s.FullName.Replace("'", "''")}','{s.Email}','{normEmail}','{s.Email}','{normEmail}',true,'{hash}','{Guid.NewGuid()}','{Guid.NewGuid()}',NULL,false,false,NULL,true,0,false,now());");
                sb.Append($"INSERT INTO public.\"AspNetUserRoles\" (\"UserId\", \"RoleId\") VALUES ('{userId}','{studentRole}');");
                sb.Append($"UPDATE public.\"Students\" SET \"IdentityUserId\" = '{userId}' WHERE Id = {s.Id};");
            }
            await _db.Database.ExecuteSqlRawAsync(sb.ToString());
        }

        _db.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    // ------------------------------------------------------------------ semesters & academic calendar
    private async Task SeedSemestersAsync()
    {
        if (await _db.Semesters.AnyAsync()) return;
        var year = await _settings.GetCurrentYearAsync();
        int start = int.TryParse(year.Name.Split('-')[0], out var sy) ? sy : DateTime.Today.Year;

        // Real-world Indian B.Tech calendar (4 years, 8 semesters).
        // Odd  : Aug 1 - Dec 15 classes | End-sem exams Dec 16-30 | 1-week break Dec 31 - Jan 9
        // Even : Jan 10 - May 15 classes | End-sem exams May 16-31
        // Summer vacation: Jun 1 - Jul 31
        var semesters = new[]
        {
            (1, "Odd",  new DateTime(start, 8, 1),  new DateTime(start, 12, 15), new DateTime(start, 12, 15), new DateTime(start, 12, 16), new DateTime(start, 12, 30), true),
            (2, "Even", new DateTime(start + 1, 1, 10), new DateTime(start + 1, 5, 15), new DateTime(start + 1, 5, 15), new DateTime(start + 1, 5, 16), new DateTime(start + 1, 5, 31), false),
            (3, "Odd",  new DateTime(start + 1, 8, 1), new DateTime(start + 1, 12, 15), new DateTime(start + 1, 12, 15), new DateTime(start + 1, 12, 16), new DateTime(start + 1, 12, 30), false),
            (4, "Even", new DateTime(start + 2, 1, 10), new DateTime(start + 2, 5, 15), new DateTime(start + 2, 5, 15), new DateTime(start + 2, 5, 16), new DateTime(start + 2, 5, 31), false),
            (5, "Odd",  new DateTime(start + 2, 8, 1), new DateTime(start + 2, 12, 15), new DateTime(start + 2, 12, 15), new DateTime(start + 2, 12, 16), new DateTime(start + 2, 12, 30), false),
            (6, "Even", new DateTime(start + 3, 1, 10), new DateTime(start + 3, 5, 15), new DateTime(start + 3, 5, 15), new DateTime(start + 3, 5, 16), new DateTime(start + 3, 5, 31), false),
            (7, "Odd",  new DateTime(start + 3, 8, 1), new DateTime(start + 3, 12, 15), new DateTime(start + 3, 12, 15), new DateTime(start + 3, 12, 16), new DateTime(start + 3, 12, 30), false),
            (8, "Even", new DateTime(start + 4, 1, 10), new DateTime(start + 4, 5, 15), new DateTime(start + 4, 5, 15), new DateTime(start + 4, 5, 16), new DateTime(start + 4, 5, 31), false),
        };
        foreach (var (num, type, startDt, endDt, clsEnd, examStart, examEnd, isCurrent) in semesters)
        {
            _db.Semesters.Add(new Semester
            {
                AcademicYearId = year.Id,
                Number = num,
                Type = type,
                StartDate = startDt,
                EndDate = endDt,
                ClassesEndDate = clsEnd,
                ExamStartDate = examStart,
                ExamEndDate = examEnd,
                IsCurrent = num == 1 && isCurrent
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task SeedAcademicCalendarAsync()
    {
        // Refresh academic year to span Jun - May (real Indian academic year)
        var year = await _db.AcademicYears.OrderByDescending(y => y.StartDate).FirstOrDefaultAsync();
        if (year == null) return;
        int start = int.TryParse(year.Name.Split('-')[0], out var sy) ? sy : DateTime.Today.Year;
        year.StartDate = new DateTime(start, 6, 1);
        year.EndDate = new DateTime(start + 1, 5, 31);
        year.IsCurrent = true;
        await _db.SaveChangesAsync();
    }

    private async Task SeedLeaveTypesAsync()
    {
        if (await _db.LeaveTypes.AnyAsync()) return;
        _db.LeaveTypes.AddRange(
            new LeaveType { Name = "Medical", DefaultDaysAllowed = 12, IsActive = true },
            new LeaveType { Name = "Casual", DefaultDaysAllowed = 6, IsActive = true },
            new LeaveType { Name = "On-Duty", DefaultDaysAllowed = 10, IsActive = true },
            new LeaveType { Name = "Maternity", DefaultDaysAllowed = 90, IsActive = true },
            new LeaveType { Name = "Paternity", DefaultDaysAllowed = 15, IsActive = true });
        await _db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ parents
    private async Task SeedParentsAsync()
    {
        var existing = await _db.Parents.Select(p => p.StudentId).ToListAsync();
        if (existing.Count > 0) { _log.LogInformation("Parents already seeded: {Count}", existing.Count); return; }

        var rows = await _db.Students.AsNoTracking().ToListAsync();
        var hash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), _o.SeedParentPassword);
        var parentRole = (await _db.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Parent))?.Id;
        if (parentRole == null)
        {
            var pr = new IdentityRole(RoleNames.Parent);
            await _roles.CreateAsync(pr);
            parentRole = pr.Id;
        }
        var existingEmails = (await _db.Users.AsNoTracking().Select(u => u.NormalizedEmail!).ToListAsync()).ToHashSet(StringComparer.Ordinal);

        // Bulk insert parents + their Identity users directly via raw SQL (bypasses UserManager round-trips).
        int added = 0;
        var sb = new System.Text.StringBuilder();
        foreach (var s in rows)
        {
            string email = $"parent.{s.StudentID}@attendx.edu.in";
            string normEmail = email.ToUpperInvariant();
            if (existingEmails.Contains(normEmail)) continue;
            existingEmails.Add(normEmail);

            var userId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            sb.Append($"INSERT INTO public.\"AspNetUsers\" (\"Id\", \"FullName\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PasswordHash\", \"SecurityStamp\", \"ConcurrencyStamp\", \"PhoneNumber\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnd\", \"LockoutEnabled\", \"AccessFailedCount\", \"MustChangePassword\", \"CreatedAt\") VALUES ('{userId}','Parent of {s.FullName.Replace("'", "''")}','{email}','{normEmail}','{email}','{normEmail}',true,'{hash}','{Guid.NewGuid()}','{Guid.NewGuid()}',NULL,false,false,NULL,true,0,false,now());");
            sb.Append($"INSERT INTO public.\"AspNetUserRoles\" (\"UserId\", \"RoleId\") VALUES ('{userId}','{parentRole}');");
            sb.Append($"INSERT INTO public.\"Parents\" (\"FullName\", \"Email\", \"Phone\", \"StudentId\", \"IdentityUserId\", \"IsActive\", \"CreatedAt\") VALUES ('Parent of {s.FullName.Replace("'", "''")}','{email}','{s.Phone}',{s.Id},'{userId}',true,now());");
            added++;
            if (added % 500 == 0)
            {
                await _db.Database.ExecuteSqlRawAsync(sb.ToString());
                sb.Clear();
                _log.LogInformation("Parents: {Count}/{Total}", added, rows.Count);
            }
        }
        if (sb.Length > 0) await _db.Database.ExecuteSqlRawAsync(sb.ToString());
        _log.LogInformation("Seeded {Count} parent logins.", added);
    }

    private async Task SeedLeaveBalancesAsync()
    {
        var year = await _settings.GetCurrentYearAsync();
        var studentIds = await _db.Students.AsNoTracking().Select(s => s.Id).ToListAsync();
        var existing = (await _db.LeaveBalances.Select(b => b.StudentId).ToListAsync()).ToHashSet();
        int added = 0;
        foreach (var sid in studentIds)
        {
            if (existing.Contains(sid)) continue;
            _db.LeaveBalances.Add(new LeaveBalance { StudentId = sid, AcademicYearId = year.Id, TotalDays = 12, UsedDays = 0 });
            added++;
        }
        if (added > 0) { await _db.SaveChangesAsync(); _log.LogInformation("Seeded leave balances for {Count} students.", added); }
    }

    // ------------------------------------------------------------------ timetable + mappings from timetable.csv
    private async Task SeedTimetableAsync()
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
        var scheduledSlots = new HashSet<(int SectionId, DayOfWeek Day, int Period, int Semester)>();
        var facultySlots = new HashSet<(int FacultyId, DayOfWeek Day, int Period, int Semester)>();
        var roomSlots = new HashSet<(string RoomNo, DayOfWeek Day, int Period, int Semester)>();
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        int n = 0;
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

            if (!scheduledSlots.Add((sec.Id, day, period, sem))) continue;
            if (!facultySlots.Add((f.Id, day, period, sem))) continue;

            string room = r["room"];
            if (!roomSlots.Add((room, day, period, sem))) continue;

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

    private async Task SeedNoticesAsync()
    {
        if (await _db.Notices.AnyAsync()) return;
        var now = DateTime.UtcNow;
        _db.Notices.AddRange(
            new Notice { Title = "Welcome to AttendX", Body = "Attendance is now digital. Faculty mark attendance in the period window; students can follow their attendance live on the student dashboard.", Audience = "All", IsPinned = true, PublishedAt = now.AddDays(-3), CreatedBy = "System Administrator" },
            new Notice { Title = "75% attendance is required", Body = "Students below 75% attendance may not be allowed to sit the end-semester exams. Check your dashboard regularly and raise a query if a record looks wrong.", Audience = "Student", PublishedAt = now.AddDays(-2), CreatedBy = "System Administrator" },
            new Notice { Title = "Mark attendance inside the period window", Body = "Attendance can be marked from 10 minutes before the period starts until 10 minutes after it ends. After that only Admin/HOD can correct it, with a reason.", Audience = "Faculty", PublishedAt = now.AddDays(-2), CreatedBy = "System Administrator" },
            new Notice { Title = "Mid-1 completed", Body = "Mid-Semester 1 examinations were held in the 3rd week of September. Results are published on your student dashboard. Mid-Sem 2 will be held in the 3rd week of November.", Audience = "All", PublishedAt = now.AddDays(-15), CreatedBy = "System Administrator" },
            new Notice { Title = "End-Semester exams", Body = "End-Semester 1 exams will be held from 16 to 30 December 2026. Bring your hall ticket and institute ID card.", Audience = "Student", PublishedAt = now.AddDays(-10), CreatedBy = "System Administrator" });
        await _db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ exams (completed + upcoming)
    private async Task SeedExamsAsync()
    {
        if (await _db.Exams.AnyAsync()) return;
        var year = await _settings.GetCurrentYearAsync();
        int start = int.TryParse(year.Name.Split('-')[0], out var sy) ? sy : DateTime.Today.Year;

        // Completed: Mid-1 (3rd week Sep 2026), Mid-2 (3rd week Nov 2026), End-Sem (Dec 16-30 2026) — for ALL current semesters.
        // Upcoming: even-semester Mid-1 (3rd week Feb 2027), Mid-2 (3rd week Mar 2027), End-Sem (May 16-31 2027).
        foreach (int sem in new[] { 1, 3, 5, 7 })
        {
            _db.Exams.AddRange(
                new Exam { Name = $"Mid-Sem 1 (Sem {sem})", Type = "Internal", Semester = sem, AcademicYearId = year.Id, StartDate = new DateTime(start, 9, 14), EndDate = new DateTime(start, 9, 19), IsActive = true },
                new Exam { Name = $"Mid-Sem 2 (Sem {sem})", Type = "Internal", Semester = sem, AcademicYearId = year.Id, StartDate = new DateTime(start, 11, 16), EndDate = new DateTime(start, 11, 21), IsActive = true },
                new Exam { Name = $"End-Semester (Sem {sem})", Type = "External", Semester = sem, AcademicYearId = year.Id, StartDate = new DateTime(start, 12, 16), EndDate = new DateTime(start, 12, 30), IsActive = true });
        }
        foreach (int sem in new[] { 2, 4, 6, 8 })
        {
            _db.Exams.AddRange(
                new Exam { Name = $"Mid-Sem 1 (Sem {sem})", Type = "Internal", Semester = sem, AcademicYearId = year.Id, StartDate = new DateTime(start + 1, 2, 15), EndDate = new DateTime(start + 1, 2, 20), IsActive = true },
                new Exam { Name = $"Mid-Sem 2 (Sem {sem})", Type = "Internal", Semester = sem, AcademicYearId = year.Id, StartDate = new DateTime(start + 1, 3, 15), EndDate = new DateTime(start + 1, 3, 20), IsActive = true },
                new Exam { Name = $"End-Semester (Sem {sem})", Type = "External", Semester = sem, AcademicYearId = year.Id, StartDate = new DateTime(start + 1, 5, 16), EndDate = new DateTime(start + 1, 5, 31), IsActive = true });
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds an exam timetable slot for every (exam, subject, section) triple without faculty/room clashes.
    /// All papers are held in the same slot per day (FN 10:00-13:00). Each faculty can invigilate at most one room per slot.
    /// Rooms and faculty are assigned per (exam, date) with dedup so the same room/faculty is never used twice on one date+slot.
    /// </summary>
    private async Task SeedExamTimetableAsync()
    {
        if (!await _db.Exams.AnyAsync()) await SeedExamsAsync();

        var year = await _settings.GetCurrentYearAsync();
        var exams = await _db.Exams.AsNoTracking().Where(e => e.IsActive && e.Type != "External").OrderBy(e => e.Semester).ThenBy(e => e.Name).ToListAsync();
        if (exams.Count == 0) return;

        var subjects = await _db.Subjects.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        var faculty = await _db.Faculty.AsNoTracking().Where(f => f.IsActive).ToListAsync();
        var sections = await _db.Sections.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        var branchById = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b);

        // Exam centers: Block A/B/C, floors 1-3, rooms 01-30 (270 rooms).
        var rooms = new List<string>();
        foreach (var blk in new[] { "Block A", "Block B", "Block C" })
            for (int f = 1; f <= 3; f++)
                for (int r = 1; r <= 30; r++)
                    rooms.Add($"{blk}-{f}{r:D2}");

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        int created = 0;

        // Pre-load existing room/faculty combos from DB to avoid clashes with already-seeded slots (e.g. Sem 1).
        var usedFacultyOnDate = new Dictionary<DateTime, HashSet<int>>();
        var usedRoomOnDate = new Dictionary<DateTime, HashSet<string>>();
        var existingRooms = await _db.ExamTimetables.AsNoTracking()
            .Select(t => new { t.RoomNo, t.ExamDate, t.StartTime })
            .ToListAsync();
        foreach (var er in existingRooms)
        {
            if (!usedRoomOnDate.TryGetValue(er.ExamDate.Date, out var rmSet)) usedRoomOnDate[er.ExamDate.Date] = rmSet = new HashSet<string>();
            rmSet.Add(er.RoomNo);
        }
        var existingFaculty = await _db.ExamTimetables.AsNoTracking()
            .Select(t => new { t.FacultyId, t.ExamDate, t.StartTime })
            .ToListAsync();
        foreach (var ef in existingFaculty)
        {
            if (!usedFacultyOnDate.TryGetValue(ef.ExamDate.Date, out var facSet)) usedFacultyOnDate[ef.ExamDate.Date] = facSet = new HashSet<int>();
            facSet.Add(ef.FacultyId);
        }

        int facultyCursor = 0;
        int roomCursor = 0;
        foreach (var exam in exams)
        {
            var existing = await _db.ExamTimetables.AsNoTracking().AnyAsync(t => t.ExamId == exam.Id);
            if (existing)
            {
                _log.LogDebug("Exam {ExamId} ({Name}) Sem {Semester} already has slots, skipping", exam.Id, exam.Name, exam.Semester);
                continue;
            }
            _log.LogInformation("Exam {ExamId} ({Name}) Sem {Semester} has no slots, generating", exam.Id, exam.Name, exam.Semester);

            var semSubjects = subjects.Where(s => s.Semester == exam.Semester).ToList();
            if (semSubjects.Count == 0) continue;
            var sectionsWithStudents = await _db.Students.AsNoTracking()
                .Where(s => s.IsActive && s.Semester == exam.Semester)
                .Select(s => new { s.BranchId, s.SectionId })
                .Distinct()
                .ToListAsync();
            var activeSectionKeys = sectionsWithStudents.Select(x => (x.BranchId, x.SectionId)).ToHashSet();
            var examSections = sections.Where(s => branchById.ContainsKey(s.BranchId) && activeSectionKeys.Contains((s.BranchId, s.Id))).ToList();
            int dayIdx = 0;

            // Per-exam section-day dedup: a section can only have one slot per day within the same exam
            var usedSectionOnDate = new Dictionary<DateTime, HashSet<int>>();

            foreach (var sub in semSubjects)
            {
                var examDate = exam.StartDate.AddDays(dayIdx % Math.Max(1, (exam.EndDate - exam.StartDate).Days + 1));
                dayIdx++;
                var sameBranchSections = examSections.Where(s => s.BranchId == sub.BranchId).ToList();
                if (sameBranchSections.Count == 0) continue;

                if (!usedFacultyOnDate.TryGetValue(examDate.Date, out var usedFac)) usedFacultyOnDate[examDate.Date] = usedFac = new HashSet<int>();
                if (!usedRoomOnDate.TryGetValue(examDate.Date, out var usedRm)) usedRoomOnDate[examDate.Date] = usedRm = new HashSet<string>();
                if (!usedSectionOnDate.TryGetValue(examDate.Date, out var usedSec)) usedSectionOnDate[examDate.Date] = usedSec = new HashSet<int>();

                foreach (var sec in sameBranchSections)
                {
                    if (usedSec.Contains(sec.Id)) continue;

                    int pick = 0;
                    while (usedFac.Contains(faculty[facultyCursor % faculty.Count].Id) && pick < faculty.Count)
                    {
                        facultyCursor++;
                        pick++;
                    }
                    var inv = faculty[facultyCursor % faculty.Count]; facultyCursor++;
                    usedFac.Add(inv.Id);

                    pick = 0;
                    while (usedRm.Contains(rooms[roomCursor % rooms.Count]) && pick < rooms.Count)
                    {
                        roomCursor++;
                        pick++;
                    }
                    var room = rooms[roomCursor % rooms.Count]; roomCursor++;
                    usedRm.Add(room);
                    usedSec.Add(sec.Id);

                    // Stagger start times per semester so sections on the same day don't clash
                    int startHour = 9 + ((exam.Semester - 1) / 2);
                    var startTime = new TimeSpan(startHour, 0, 0);
                    var endTime = startTime.Add(TimeSpan.FromHours(3));

                    _db.ExamTimetables.Add(new ExamTimetable
                    {
                        ExamId = exam.Id, SubjectId = sub.Id, BranchId = sec.BranchId, SectionId = sec.Id,
                        Semester = exam.Semester, FacultyId = inv.Id,
                        ExamDate = examDate, StartTime = startTime, EndTime = endTime,
                        RoomNo = room, IsActive = true
                    });
                    created++;
                    if (created % 500 == 0) { await _db.SaveChangesAsync(); _db.ChangeTracker.Clear(); }
                }
            }
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _db.ChangeTracker.AutoDetectChangesEnabled = true;
        _log.LogInformation("Seeded {Count} exam timetable slots.", created);
    }

    /// <summary>
    /// Generates exam timetable slots ONLY for exams that currently have zero slots,
    /// without touching existing data. Used to add Mid-1/Mid-2 slots for odd semesters
    /// that were missed during the original seed.
    /// </summary>
    internal async Task GenerateMissingExamTimetablesAsync()
    {
        var year = await _settings.GetCurrentYearAsync();
        var exams = await _db.Exams.AsNoTracking()
            .Where(e => e.IsActive && e.Type != "External" && e.Semester % 2 == 1)
            .OrderBy(e => e.Semester).ThenBy(e => e.Name)
            .ToListAsync();
        _log.LogInformation("SeedExamTimetableAsync: found {ExamCount} exams", exams.Count);
        if (exams.Count == 0) return;

        var subjects = await _db.Subjects.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        var faculty = await _db.Faculty.AsNoTracking().Where(f => f.IsActive).ToListAsync();
        var sections = await _db.Sections.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        var branchById = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b);

        var rooms = new List<string>();
        foreach (var blk in new[] { "Block A", "Block B", "Block C" })
            for (int f = 1; f <= 3; f++)
                for (int r = 1; r <= 30; r++)
                    rooms.Add($"{blk}-{f}{r:D2}");

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        int created = 0;

        // Global dedup across ALL exams being generated AND existing DB rows
        int facultyCursor = 0;
        int roomCursor = 0;
        var usedFacultyOnDate = new Dictionary<DateTime, HashSet<int>>();
        var usedRoomOnDate = new Dictionary<DateTime, HashSet<string>>();

        // Pre-load existing room/date combos from DB to avoid conflicts with Sem 1's slots
        var existingRooms = await _db.ExamTimetables.AsNoTracking()
            .Select(t => new { t.RoomNo, t.ExamDate, t.StartTime })
            .ToListAsync();
        foreach (var er in existingRooms)
        {
            if (!usedRoomOnDate.TryGetValue(er.ExamDate.Date, out var rmSet)) usedRoomOnDate[er.ExamDate.Date] = rmSet = new HashSet<string>();
            rmSet.Add(er.RoomNo);
        }
        var existingFaculty = await _db.ExamTimetables.AsNoTracking()
            .Select(t => new { t.FacultyId, t.ExamDate, t.StartTime })
            .ToListAsync();
        foreach (var ef in existingFaculty)
        {
            if (!usedFacultyOnDate.TryGetValue(ef.ExamDate.Date, out var facSet)) usedFacultyOnDate[ef.ExamDate.Date] = facSet = new HashSet<int>();
            facSet.Add(ef.FacultyId);
        }

        foreach (var exam in exams)
        {
            var existing = await _db.ExamTimetables.AsNoTracking().AnyAsync(t => t.ExamId == exam.Id);
            if (existing) continue; // already has slots

            var semSubjects = subjects.Where(s => s.Semester == exam.Semester).ToList();
            if (semSubjects.Count == 0) continue;

            var sectionsWithStudents = await _db.Students.AsNoTracking()
                .Where(s => s.IsActive && s.Semester == exam.Semester)
                .Select(s => new { s.BranchId, s.SectionId })
                .Distinct()
                .ToListAsync();
            var activeSectionKeys = sectionsWithStudents.Select(x => (x.BranchId, x.SectionId)).ToHashSet();
            var examSections = sections.Where(s => branchById.ContainsKey(s.BranchId) && activeSectionKeys.Contains((s.BranchId, s.Id))).ToList();
            int dayIdx = 0;

            // Per-exam section-day dedup: a section can only have one slot per day within the same exam
            var usedSectionOnDate = new Dictionary<DateTime, HashSet<int>>();

            foreach (var sub in semSubjects)
            {
                var examDate = exam.StartDate.AddDays(dayIdx % Math.Max(1, (exam.EndDate - exam.StartDate).Days + 1));
                dayIdx++;
                var sameBranchSections = examSections.Where(s => s.BranchId == sub.BranchId).ToList();
                if (sameBranchSections.Count == 0) continue;

                if (!usedFacultyOnDate.TryGetValue(examDate.Date, out var usedFac)) usedFacultyOnDate[examDate.Date] = usedFac = new HashSet<int>();
                if (!usedRoomOnDate.TryGetValue(examDate.Date, out var usedRm)) usedRoomOnDate[examDate.Date] = usedRm = new HashSet<string>();
                if (!usedSectionOnDate.TryGetValue(examDate.Date, out var usedSec)) usedSectionOnDate[examDate.Date] = usedSec = new HashSet<int>();

                foreach (var sec in sameBranchSections)
                {
                    if (usedSec.Contains(sec.Id)) continue;

                    int pick = 0;
                    while (usedFac.Contains(faculty[facultyCursor % faculty.Count].Id) && pick < faculty.Count)
                    {
                        facultyCursor++;
                        pick++;
                    }
                    var inv = faculty[facultyCursor % faculty.Count]; facultyCursor++;
                    usedFac.Add(inv.Id);

                    pick = 0;
                    while (usedRm.Contains(rooms[roomCursor % rooms.Count]) && pick < rooms.Count)
                    {
                        roomCursor++;
                        pick++;
                    }
                    var room = rooms[roomCursor % rooms.Count]; roomCursor++;
                    usedRm.Add(room);
                    usedSec.Add(sec.Id);

                    _db.ExamTimetables.Add(new ExamTimetable
                    {
                        ExamId = exam.Id, SubjectId = sub.Id, BranchId = sec.BranchId, SectionId = sec.Id,
                        Semester = exam.Semester, FacultyId = inv.Id,
                        ExamDate = examDate, StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(13, 0, 0),
                        RoomNo = room, IsActive = true
                    });
                    created++;
                    if (created % 500 == 0) { await _db.SaveChangesAsync(); _db.ChangeTracker.Clear(); }
                }
            }
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _db.ChangeTracker.AutoDetectChangesEnabled = true;
        _log.LogInformation("Generated {Count} missing exam timetable slots.", created);
    }

    private async Task SeedExamRegistrationsAsync()
    {
        var settings = await _settings.GetAsync();
        var exams = await _db.Exams.AsNoTracking().ToListAsync();
        if (exams.Count == 0) return;
        var students = await _db.Students.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        var attendance = await _db.Attendance.AsNoTracking()
            .GroupBy(a => a.StudentId)
            .Select(g => new { StudentId = g.Key, Total = g.Count(), Present = g.Count(x => x.Status == AttendanceStatus.Present), Late = g.Count(x => x.Status == AttendanceStatus.Late) })
            .ToListAsync();
        var pctByStudent = attendance.ToDictionary(a => a.StudentId, a => AttendanceMath.Percent(a.Present, a.Late, a.Total, settings.LateWeight));

        int created = 0;
        foreach (var exam in exams)
        {
            var existing = await _db.ExamRegistrations.AsNoTracking().AnyAsync(r => r.ExamId == exam.Id);
            if (existing) continue;

            foreach (var s in students.Where(st => st.Semester == exam.Semester))
            {
                var pct = pctByStudent.TryGetValue(s.Id, out var p) ? p : 100m;
                var eligible = pct >= settings.DefaultThreshold;
                _db.ExamRegistrations.Add(new ExamRegistration
                {
                    ExamId = exam.Id, StudentId = s.Id, IsEligible = eligible,
                    EligibilityReason = eligible ? null : $"Attendance {pct}% is below required {settings.DefaultThreshold}%.",
                    RegisteredAt = DateTime.UtcNow, HallTicketGenerated = eligible
                });
                created++;
            }
        }
        await _db.SaveChangesAsync();
        _log.LogInformation("Seeded {Count} exam registrations.", created);
    }

    private async Task SeedExamMarksAsync()
    {
        var year = await _settings.GetCurrentYearAsync();
        int start = int.TryParse(year.Name.Split('-')[0], out var sy) ? sy : DateTime.Today.Year;
        // Completed as of today (Nov 16 2026): Mid-Sem 1 (3rd week Sep) and Mid-Sem 2 (3rd week Nov).
        // End-Semester (Dec 16-30 2026) has NOT happened yet — its marks must not be fabricated.
        var completedExamNames = new[] { $"Mid-Sem 1 (Sem 1)", $"Mid-Sem 1 (Sem 3)", $"Mid-Sem 1 (Sem 5)", $"Mid-Sem 1 (Sem 7)", $"Mid-Sem 2 (Sem 1)", $"Mid-Sem 2 (Sem 3)", $"Mid-Sem 2 (Sem 5)", $"Mid-Sem 2 (Sem 7)" };
        var completed = await _db.Exams.AsNoTracking().Where(e => completedExamNames.Contains(e.Name)).ToListAsync();
        if (completed.Count == 0) return;
        var completedIds = completed.Select(e => e.Id).ToHashSet();

        var examTimetables = await _db.ExamTimetables.AsNoTracking().Include(tt => tt.Exam).Where(tt => completedIds.Contains(tt.ExamId)).ToListAsync();
        if (examTimetables.Count == 0) return;

        // Skip timetables that already have marks
        var ttIdsWithMarks = await _db.ExamMarks.AsNoTracking().Select(m => m.ExamTimetableId).ToListAsync();
        examTimetables = examTimetables.Where(tt => !ttIdsWithMarks.Contains(tt.Id)).ToList();
        if (examTimetables.Count == 0) return;

        var students = await _db.Students.AsNoTracking().Where(s => s.IsActive).ToListAsync();
        var studentByClass = students.GroupBy(s => new { s.BranchId, s.SectionId, s.Semester }).ToDictionary(g => g.Key, g => g.ToList());
        var rnd = new Random(2027);
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        int created = 0;
        foreach (var tt in examTimetables)
        {
            if (!studentByClass.TryGetValue(new { tt.BranchId, tt.SectionId, tt.Semester }, out var list)) continue;
            bool isExternal = completed.Any(e => e.Id == tt.ExamId && e.Type == "External");
            // For completed exams, seed marks only for eligible-ish students (90% attendance band)
            foreach (var s in list)
            {
                decimal internalMark = Math.Round((decimal)(rnd.NextDouble() * 12 + 18), 2);   // 18-30
                decimal externalMark = isExternal ? Math.Round((decimal)(rnd.NextDouble() * 35 + 35), 2) : Math.Round((decimal)(rnd.NextDouble() * 10 + 20), 2);   // 35-70 for external, 20-30 for internal-only
                if (!isExternal) externalMark = 0m;   // internal exams have no external component
                string? grade = GradeFor(internalMark + externalMark, isExternal);
                string lockStatus = isExternal ? "Locked" : "Submitted";
                _db.ExamMarks.Add(new ExamMark
                {
                    ExamTimetableId = tt.Id, StudentId = s.Id,
                    InternalMark = internalMark, ExternalMark = externalMark, Grade = grade,
                    Remarks = null, MarkedBy = "Faculty",
                    MarkedAt = tt.ExamDate.AddDays(2), LastModifiedAt = null,
                    LockStatus = lockStatus, LockedAt = isExternal ? tt.ExamDate.AddDays(3) : null, LockedBy = isExternal ? "Branch HOD" : null
                });
                created++;
                if (created % 1000 == 0) { await _db.SaveChangesAsync(); _db.ChangeTracker.Clear(); }
            }
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _db.ChangeTracker.AutoDetectChangesEnabled = true;
        _log.LogInformation("Seeded {Count} exam marks.", created);
    }

    private static string? GradeFor(decimal total, bool isExternal)
    {
        if (!isExternal) return null;
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

    // ------------------------------------------------------------------ attendance history (raw SQL bulk insert, preserves existing data)
    private async Task SeedAttendanceHistoryAsync()
    {
        // Attendance only makes sense for currently-running odd semesters (Aug–Dec 2026: Sem 1, 3, 5, 7).
        // Even semesters start Jan 2027 — no attendance to seed yet.
        var tts = await _db.Timetables.AsNoTracking().Where(t => t.Semester == 1 || t.Semester == 3 || t.Semester == 5 || t.Semester == 7).ToListAsync();
        var activeSemesters = new HashSet<int> { 1, 3, 5, 7 };
        var students = await _db.Students.AsNoTracking().Where(s => activeSemesters.Contains(s.Semester)).Select(s => new { s.Id, s.BranchId, s.SectionId, s.Semester }).ToListAsync();
        if (tts.Count == 0 || students.Count == 0) return;

        // Skip dates that already have attendance sessions (preserves existing Oct 16+ data)
        var existingDates = new HashSet<DateTime>();
        var maxSessionIds = await _db.AttendanceSessions.Select(s => (DateTime?)s.Date).ToListAsync();
        foreach (var d in maxSessionIds) if (d.HasValue) existingDates.Add(d.Value.Date);

        var rnd = new Random(2026);
        var startDate = new DateTime(2026, 8, 1);
        var endDate = DateTime.Today;

        var holidays = await new CalendarService(_db, new SystemClock()).HolidayDatesAsync(startDate, endDate);
        var byClass = students.GroupBy(s => (s.BranchId, s.SectionId, s.Semester)).ToDictionary(g => g.Key, g => g.ToList());
        var prop = students.ToDictionary(s => s.Id, s => { var r = rnd.NextDouble(); return r < 0.2 ? 0.55 + rnd.NextDouble() * 0.17 : r < 0.8 ? 0.78 + rnd.NextDouble() * 0.12 : 0.92 + rnd.NextDouble() * 0.06; });
        var decliners = students.Where(_ => rnd.NextDouble() < 0.08).Select(s => s.Id).ToHashSet();

        var days = Enumerable.Range(0, (endDate - startDate).Days + 1).Select(i => startDate.AddDays(i))
            .Where(d => d.DayOfWeek != DayOfWeek.Sunday && !holidays.Contains(d) && !existingDates.Contains(d))
            .OrderBy(d => d).ToList();

        if (days.Count == 0)
        {
            _log.LogInformation("Attendance already seeded for Aug 1 - today");
            return;
        }
        if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeding {Days} days of attendance history (Aug 1 - today)...", days.Count);

        int maxSessionId = await _db.AttendanceSessions.MaxAsync(s => (int?)s.Id) ?? 0;
        long maxAttendanceId = await _db.Attendance.MaxAsync(a => (long?)a.Id) ?? 0;

        var yearId = await _db.AcademicYears.Select(a => (int?)a.Id).FirstOrDefaultAsync() ?? 0;
        var adminUserId = await _db.Users.Select(u => (string?)u.Id).FirstOrDefaultAsync() ?? "";

        _db.ChangeTracker.AutoDetectChangesEnabled = false;

        int totalSessions = 0, totalAttendance = 0;
        var chunkSessionIds = new List<int>();
        foreach (var chunk in days.Chunk(100))
        {
            var sessionSb = new System.Text.StringBuilder("INSERT INTO public.\"AttendanceSessions\" (\"Id\", \"TimetableId\", \"AcademicYearId\", \"Date\", \"Period\", \"SubjectId\", \"SectionId\", \"FacultyId\", \"Mode\", \"DeviceId\", \"StartedByUserId\", \"StartedAt\") VALUES ");
            var sessionRows = new List<string>();

            foreach (var date in chunk)
            {
                foreach (var t in tts.Where(t => t.Day == date.DayOfWeek))
                {
                    var sid = maxSessionId + 1 + chunkSessionIds.Count;
                    chunkSessionIds.Add(sid);
                    var startedAt = date + t.StartTime;
                    sessionRows.Add($"({sid},{t.Id},{yearId},'{date:yyyy-MM-dd}',{t.Period},{t.SubjectId},{t.SectionId},{t.FacultyId},'Manual','seed',NULL,CURRENT_TIMESTAMP)");
                }
            }
            sessionSb.Append(string.Join(",", sessionRows));
            await _db.Database.ExecuteSqlRawAsync(sessionSb.ToString());
            totalSessions += chunkSessionIds.Count;

            var attSb = new System.Text.StringBuilder("INSERT INTO public.\"Attendance\" (\"Id\", \"StudentId\", \"SubjectId\", \"FacultyId\", \"SectionId\", \"BranchId\", \"Semester\", \"Date\", \"Period\", \"Status\", \"MarkedAt\", \"AttendanceSessionId\", \"Source\", \"DeviceId\", \"RowVersion\") VALUES ");
            var attRows = new List<string>();
            int sessionIdx = 0;

            foreach (var date in chunk)
            {
                foreach (var t in tts.Where(t => t.Day == date.DayOfWeek))
                {
                    if (!byClass.TryGetValue((t.BranchId, t.SectionId, t.Semester), out var list)) continue;
                    foreach (var s in list)
                    {
                        double p = prop[s.Id];
                        if (decliners.Contains(s.Id) && date >= endDate.AddDays(-14)) p *= 0.55;
                        string status = rnd.NextDouble() < p ? (rnd.NextDouble() < 0.06 ? "Late" : "Present") : "Absent";
                        var statusVal = status == "Present" ? 1 : status == "Late" ? 3 : 2;
                        var markedAt = date + t.StartTime.Add(TimeSpan.FromMinutes(5));
                        attRows.Add($"({maxAttendanceId + 1 + attRows.Count},{s.Id},{t.SubjectId},{t.FacultyId},{t.SectionId},{s.BranchId},{s.Semester},'{date:yyyy-MM-dd}',{t.Period},{statusVal},CURRENT_TIMESTAMP,{chunkSessionIds[sessionIdx]},'Manual','seed',decode('00','hex'))");
                        totalAttendance++;

                        // Flush in batches to avoid oversized INSERT statements on pooled connections
                        if (attRows.Count >= 1000)
                        {
                            attSb.Append(string.Join(",", attRows));
                            await _db.Database.ExecuteSqlRawAsync(attSb.ToString());
                            maxAttendanceId += attRows.Count;
                            attRows.Clear();
                            attSb = new System.Text.StringBuilder("INSERT INTO public.\"Attendance\" (\"Id\", \"StudentId\", \"SubjectId\", \"FacultyId\", \"SectionId\", \"BranchId\", \"Semester\", \"Date\", \"Period\", \"Status\", \"MarkedAt\", \"AttendanceSessionId\", \"Source\", \"DeviceId\", \"RowVersion\") VALUES ");
                        }
                    }
                    sessionIdx++;
                }
            }
            // Flush remaining rows
            if (attRows.Count > 0)
            {
                attSb.Append(string.Join(",", attRows));
                await _db.Database.ExecuteSqlRawAsync(attSb.ToString());
                maxAttendanceId += attRows.Count;
            }

            if (totalSessions % 1000 < 100)
                Console.WriteLine($"Progress: {totalSessions} sessions, {totalAttendance} attendance rows");
        }

        _db.ChangeTracker.AutoDetectChangesEnabled = true;
        _log.LogInformation("Seeded {Sessions} sessions and {Attendance} attendance records (Aug 1 - today).", totalSessions, totalAttendance);
    }

    private async Task FixMalformedStudentIdsAsync()
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
            string expectedEmail = $"{cleanName}.{s.StudentID}@attendx.edu.in";
            if (s.Email != expectedEmail)
            {
                s.Email = expectedEmail;
                changed = true;
            }
            idx++;
        }
        if (changed) await _db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ self check (logged; also shown on the Health page)
    private async Task SelfCheckAsync()
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

