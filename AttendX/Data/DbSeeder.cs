using System.Data;
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
    private static readonly Dictionary<string, (string Prefix, int Count)> SectionRules = new()
    {
        ["CSE"] = ("EV", 6), ["CSEAI"] = ("EN", 24), ["CSEAIML"] = ("EN", 24), ["ADS"] = ("EU", 6), ["ECE"] = ("EC", 6), ["EEE"] = ("EE", 4),
        ["MECH"] = ("ME", 4), ["CIVIL"] = ("CE", 4), ["IT"] = ("IT", 6), ["CYBER"] = ("CS", 6), ["AIDS"] = ("AD", 4), ["AIML"] = ("AM", 4), ["IOT"] = ("IO", 4), ["DS"] = ("DS", 4)
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
    private readonly bool _usePostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false;

    private string Csv(string name) => Path.Combine(_env.ContentRootPath, "Dataset", name);
    private List<Dictionary<string, string>> Read(string name) => File.Exists(Csv(name)) ? CsvParser.ParseWithHeader(File.ReadAllText(Csv(name))) : [];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (_o.UseMigrations) await _db.Database.MigrateAsync(cancellationToken);
        else await EnsureDatabaseSchemaAsync(cancellationToken);

        // Ensure ExamTimetables table exists for Neon Postgres
        if (_usePostgres)
        {
            try
            {
                await _db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS ""ExamTimetables"" (
                        ""Id"" serial PRIMARY KEY,
                        ""AcademicYearId"" integer NOT NULL REFERENCES ""AcademicYears""(""Id"") ON DELETE RESTRICT,
                        ""BranchId"" integer NOT NULL REFERENCES ""Branches""(""Id"") ON DELETE RESTRICT,
                        ""SectionId"" integer REFERENCES ""Sections""(""Id"") ON DELETE RESTRICT,
                        ""Semester"" integer NOT NULL,
                        ""SubjectId"" integer NOT NULL REFERENCES ""Subjects""(""Id"") ON DELETE RESTRICT,
                        ""FacultyId"" integer NOT NULL REFERENCES ""Faculty""(""Id"") ON DELETE RESTRICT,
                        ""ExamType"" character varying(20) NOT NULL,
                        ""ExamDate"" timestamp with time zone NOT NULL,
                        ""StartTime"" interval NOT NULL,
                        ""EndTime"" interval NOT NULL,
                        ""RoomNo"" character varying(30) NOT NULL DEFAULT '',
                        ""IsActive"" boolean NOT NULL DEFAULT TRUE
                    );
                    CREATE INDEX IF NOT EXISTS ""IX_ExamTimetables_Semester_Type_Date"" ON ""ExamTimetables"" (""AcademicYearId"", ""Semester"", ""ExamType"", ""ExamDate"");
                    CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ExamTimetables_Unique"" ON ""ExamTimetables"" (""AcademicYearId"", ""BranchId"", ""Semester"", ""SubjectId"", ""ExamType"") WHERE ""IsActive"";
                ");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "ExamTimetables creation check failed, continuing");
            }
        }

        foreach (var r in RoleNames.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _roles.RoleExistsAsync(r)) await _roles.CreateAsync(new IdentityRole(r));
        }

        await _settings.GetCurrentYearAsync();
        await _settings.GetAsync();
        await SeedPeriodsAsync(cancellationToken);
        await SeedBranchesAndSectionsAsync(cancellationToken);
        await SeedAdminAsync(cancellationToken);
        await SeedSubjectsAsync(cancellationToken);
        await FixMalformedStudentIdsAsync(cancellationToken);

        // Force rebuild check: if CSV counts != DB counts, force DELETE and reseed all people + attendance
        var facultyCsvCount = Read("faculty.csv").Count;
        var studentsCsvCount = Read("students.csv").Count;
        var facultyDbCount = await _db.Faculty.CountAsync(cancellationToken);
        var studentsDbCount = await _db.Students.CountAsync(cancellationToken);
        var attendanceCount = await _db.Attendance.CountAsync(a => a.Date >= new DateTime(2026, 8, 1), cancellationToken);

        bool needsRebuild = facultyDbCount < facultyCsvCount || studentsDbCount < studentsCsvCount || attendanceCount < 5000;

        if (needsRebuild)
        {
            if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Force rebuild needed: Faculty DB {F} vs CSV {CF}, Students DB {S} vs CSV {CS}, Attendance Aug-Nov {A}", facultyDbCount, facultyCsvCount, studentsDbCount, studentsCsvCount, attendanceCount);
            // Truncate in FK-safe order (children first) to avoid duplicates/missing
            await _db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""Attendance"";
                DELETE FROM ""AttendanceSessions"";
                DELETE FROM ""ExamTimetables"";
                DELETE FROM ""Timetables"";
                DELETE FROM ""FacultySubjectMappings"";
                DELETE FROM ""Students"";
                DELETE FROM ""Faculty"";
                DELETE FROM ""AspNetUserRoles"";
                DELETE FROM ""AspNetUserTokens"";
                DELETE FROM ""AspNetUserClaims"";
                DELETE FROM ""AspNetUserLogins"";
                DELETE FROM ""AspNetUsers"";
            ");
            _log.LogInformation("Truncated tables in FK-safe order for full rebuild.");
        }

        // If faculty and students are fully seeded AND attendance coverage is complete, skip heavy seeding
        if (await _db.Faculty.CountAsync(cancellationToken) >= 120 && await _db.Students.CountAsync(cancellationToken) >= 2500 && await _db.Users.CountAsync(cancellationToken) > 2500 && attendanceCount >= 5000)
        {
            if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Database already seeded. Skipping demo seeding.");
            // Ensure exam timetables exist even if attendance is seeded
            await SeedExamTimetableAsync(cancellationToken);
            return;
        }

        if (!_o.SeedDemoData) return;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await SeedHolidaysAsync(cancellationToken);
        await SeedFacultyAsync(cancellationToken);
        await SeedStudentsAsync(cancellationToken);
        await SeedTimetableAsync(cancellationToken);
        await SeedNoticesAsync(cancellationToken);
        await SeedAttendanceHistoryAsync(cancellationToken);
        await SeedExamTimetableAsync(cancellationToken);
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

    /// <summary>
    /// EnsureCreated only builds the schema when the database does not exist at all, so a database created by an older
    /// model (renamed or missing tables/columns) would never be fixed. Every table is rebuilt from the Dataset files,
    /// so when the stored schema does not match the current model the database is dropped and created again.
    /// </summary>
    private async Task EnsureDatabaseSchemaAsync(CancellationToken cancellationToken = default)
    {
        // When running against a Supabase/Railway database that may already contain tables, EF Core migrations handle
        // both brand-new databases and databases with drifted/already-existing tables gracefully. We prefer this path
        // over raw CreateTablesAsync because EnsureCreatedAsync throws when tables already exist.
        try
        {
            await _db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            if (_usePostgres)
            {
                // In Postgres, if tables already exist (e.g., Error 42P07), fallback to EnsureCreatedAsync without throwing
                try
                {
                    await _db.Database.EnsureCreatedAsync();
                }
                catch (NpgsqlException)
                {
                    // Existing tables already present, continue safely with seeding
                }
            }
            else if (!_o.RecreateIfSchemaMismatch)
            {
                throw new InvalidOperationException($"Database schema migration failed: {ex.Message}. Set AttendX:RecreateIfSchemaMismatch to true.");
            }
        }

        if (!_usePostgres)
        {
            // Only on SQL Server do we force-create if migrations reported nothing pending but tables are missing.
            var creator = _db.Database.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
            if (await creator.ExistsAsync())
            {
                // Check whether all expected tables exist; if any are missing AND the user opts into rebuild, drop & recreate.
                var missing = await MissingSchemaObjectsAsync();
                if (missing.Count > 0 && _o.RecreateIfSchemaMismatch)
                {
                    await creator.EnsureDeletedAsync();
                    await creator.CreateAsync();
                    await creator.CreateTablesAsync();
                }
            }
        }
    }

    private async Task<List<string>> MissingSchemaObjectsAsync(CancellationToken cancellationToken = default)
    {
        var expected = _db.Model.GetEntityTypes()
            .SelectMany(e =>
            {
                var table = e.GetTableName();
                return table == null ? [] : e.GetProperties().Select(p => $"{table.ToLowerInvariant()}.{p.GetColumnName().ToLowerInvariant()}");
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ISet<string> actual;
        if (_usePostgres)
        {
            actual = (await _db.Database.SqlQueryRaw<string>(
                    @"SELECT lower(table_name) || '.' || lower(column_name) AS ""Value""
                      FROM information_schema.columns
                      WHERE table_schema = 'public'")
                .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            actual = (await _db.Database.SqlQueryRaw<string>(
                    @"SELECT lower(t.name) + '.' + lower(c.name) AS [Value]
                      FROM sys.tables t INNER JOIN sys.columns c ON c.object_id = t.object_id")
                .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // Detect both missing columns (expected but not actual) and extra columns (actual but not expected)
        var missing = expected.Except(actual, StringComparer.OrdinalIgnoreCase);
        var extra = actual.Except(expected, StringComparer.OrdinalIgnoreCase);
        return [.. missing.Union(extra).OrderBy(x => x)];
    }

    private async Task SeedPeriodsAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.PeriodDefinitions.AnyAsync()) return;
        var starts = new[] { "09:00", "10:00", "11:00", "12:00", "14:00", "15:00", "16:00" };
        for (int i = 0; i < Math.Min(_o.PeriodCount, starts.Length); i++)
        {
            var s = TimeSpan.Parse(starts[i]);
            _db.PeriodDefinitions.Add(new PeriodDefinition { PeriodNo = i + 1, StartTime = s, EndTime = s.Add(TimeSpan.FromMinutes(55)) });
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

    private async Task SeedAdminAsync(CancellationToken cancellationToken = default)
    {
        if (await _users.FindByEmailAsync(_o.AdminEmail) != null) return;
        var admin = new ApplicationUser { UserName = _o.AdminEmail, Email = _o.AdminEmail, EmailConfirmed = true, FullName = "System Administrator", MustChangePassword = _o.ForceAdminPasswordChange, LockoutEnabled = true };
        var res = await _users.CreateAsync(admin, _o.AdminPassword);
        if (res.Succeeded) await _users.AddToRoleAsync(admin, RoleNames.Admin);
        else _log.LogError("Could not create admin: {Errors}", string.Join("; ", res.Errors.Select(e => e.Description)));
    }

    private async Task SeedSubjectsAsync(CancellationToken cancellationToken = default)
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

    private async Task SeedHolidaysAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.Holidays.AnyAsync()) return;
        foreach (var r in Read("holidays.csv"))
            _db.Holidays.Add(new Holiday { Date = DateTime.Parse(r["date"]).Date, Name = r["name"], Type = r["type"] });
        await _db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ people (fast path)
    private static ApplicationUser NewUser(string email, string fullName, string hash) => new()
    {
        Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true, PasswordHash = hash, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(),
        LockoutEnabled = true, FullName = fullName, MustChangePassword = false
    };

    private async Task SeedFacultyAsync(CancellationToken cancellationToken = default)
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

    private async Task SeedStudentsAsync(CancellationToken cancellationToken = default)
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
                        Year = YearHelper.FromSemester(sem), JoiningDate = DateTime.Parse(r["joiningdate"]).Date, QRPath = "on-demand", IsActive = true, IdentityUserId = user.Id,
                        RowVersion = Array.Empty<byte>()
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
            foreach (int sem in new[] { 1, 3, 5 })
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
                        IdentityUserId = user.Id,
                        RowVersion = Array.Empty<byte>()
                    });
                }
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
            }
        }

        // Ensure every student has a login
        var allStudents = await _db.Students.ToListAsync();
        foreach (var s in allStudents)
        {
            var u = s.IdentityUserId != null ? await _users.FindByIdAsync(s.IdentityUserId) : await _users.FindByEmailAsync(s.Email);
            if (u == null)
            {
                u = NewUser(s.Email, s.FullName, hash);
                await _users.CreateAsync(u, _o.SeedStudentPassword);
                await _users.AddToRoleAsync(u, RoleNames.Student);
            }
            else
            {
                if (string.IsNullOrEmpty(u.PasswordHash) || u.MustChangePassword)
                {
                    u.PasswordHash = hash;
                    u.MustChangePassword = false;
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

            if (!scheduledSlots.Add((sec.Id, day, period))) continue;
            if (!facultySlots.Add((f.Id, day, period))) continue;

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
        var now = DateTime.Now;
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

        var startDate = new DateTime(2026, 8, 1);
        var today = DateTime.Today;
        if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeding attendance from {Start} to {Today} (real timetable mapping)...", startDate.ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd"));

        var rnd = new Random(2026);
        var holidays = await new CalendarService(_db, new SystemClock()).HolidayDatesAsync(startDate, today);
        var byClass = students.GroupBy(s => (s.BranchId, s.SectionId, s.Semester)).ToDictionary(g => g.Key, g => g.ToList());
        var prop = students.ToDictionary(s => s.Id, s => { var r = rnd.NextDouble(); return r < 0.2 ? 0.55 + rnd.NextDouble() * 0.17 : r < 0.8 ? 0.78 + rnd.NextDouble() * 0.12 : 0.92 + rnd.NextDouble() * 0.06; });
        var decliners = students.Where(_ => rnd.NextDouble() < 0.08).Select(s => s.Id).ToHashSet();

        var totalDays = (int)(today - startDate).TotalDays;
        var jobs = Enumerable.Range(0, totalDays).Select(i => startDate.AddDays(i))
            .Where(d => d.DayOfWeek != DayOfWeek.Sunday && !holidays.Contains(d))
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

    private async Task SeedExamTimetableAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.ExamTimetables.AnyAsync(cancellationToken)) return;

        var academics = await _db.AcademicYears.ToListAsync(cancellationToken);
        // Ensure 2 academic years exist
        if (academics.Count < 2)
        {
            if (!academics.Any(a => a.Name == "2026-27"))
            {
                academics.Add(new AcademicYear { Name = "2026-27", StartDate = new DateTime(2026, 8, 1), EndDate = new DateTime(2027, 5, 31), IsCurrent = true });
            }
            if (!academics.Any(a => a.Name == "2027-28"))
            {
                academics.Add(new AcademicYear { Name = "2027-28", StartDate = new DateTime(2027, 8, 1), EndDate = new DateTime(2028, 5, 31), IsCurrent = false });
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

        List<(DateTime Start, DateTime End, string Type)> examWindows = new();
        foreach (var yearData in academics)
        {
            int y = int.TryParse(yearData.Name.Split('-')[0], out int startY) ? startY : 2026;
            // Mid1: Sep 22-Oct 4, Mid2: Dec 8-Dec 20, Final: Apr 15-May 5
            examWindows.Add((new DateTime(y, 9, 22), new DateTime(y, 10, 4), "Mid1"));
            examWindows.Add((new DateTime(y, 12, 8), new DateTime(y, 12, 20), "Mid2"));
            examWindows.Add((new DateTime(y + 1, 4, 15), new DateTime(y + 1, 5, 5), "Final"));
        }

        var exams = new List<ExamTimetable>();
        var rnd = new Random(2026);
        foreach (var year in academics)
        {
            var windows = examWindows.Where(w => w.Start.Year == int.Parse(year.Name.Split('-')[0]) || w.Start.Year == int.Parse(year.Name.Split('-')[0]) + 1926).ToList();
            // Simpler: use year.Name to compute windows
            int startY = int.Parse(year.Name.Split('-')[0]);
            var windowsForYear = new List<(DateTime Start, DateTime End, string Type)>
            {
                (new DateTime(startY, 9, 22), new DateTime(startY, 10, 4), "Mid1"),
                (new DateTime(startY, 12, 8), new DateTime(startY, 12, 20), "Mid2"),
                (new DateTime(startY + 1, 4, 15), new DateTime(startY + 1, 5, 5), "Final")
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

                    exams.Add(new ExamTimetable
                    {
                        AcademicYearId = year.Id,
                        BranchId = subj.BranchId,
                        Semester = subj.Semester,
                        SubjectId = subj.Id,
                        FacultyId = facId,
                        ExamType = w.Type,
                        ExamDate = date,
                        StartTime = defaultStart,
                        EndTime = defaultEnd,
                        RoomNo = "Exam Hall A",
                        IsActive = true
                    });
                }
            }
        }

        _db.ExamTimetables.AddRange(exams);
        await _db.SaveChangesAsync(cancellationToken);
        if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeded {Count} exam timetable entries.", exams.Count);
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
