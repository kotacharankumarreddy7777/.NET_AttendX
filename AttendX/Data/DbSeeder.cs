using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
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
        ["CSE"] = ("EV", 6), ["CSEAI"] = ("EN", 24), ["CSEAIML"] = ("EN", 24), ["ADS"] = ("EU", 6), ["ECE"] = ("EC", 6), ["EEE"] = ("EE", 4),
        ["MECH"] = ("ME", 4), ["CIVIL"] = ("CE", 4), ["IT"] = ("IT", 6), ["CYBER"] = ("CS", 6), ["AIDS"] = ("AD", 4), ["AIML"] = ("AM", 4), ["IOT"] = ("IO", 4), ["DS"] = ("DS", 4)
    };

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
        if (!_o.SeedDemoData) return;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await SeedHolidaysAsync();
        await SeedFacultyAsync();
        await SeedStudentsAsync();
        await SeedTimetableAsync();
        await SeedNoticesAsync();
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
                @"SELECT t.name + '.' + c.name AS [Value]
                  FROM sys.tables t INNER JOIN sys.columns c ON c.object_id = t.object_id")
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
        var starts = new[] { "09:00", "10:00", "11:00", "12:00", "14:00", "15:00", "16:00" };
        for (int i = 0; i < Math.Min(_o.PeriodCount, starts.Length); i++)
        {
            var s = TimeSpan.Parse(starts[i]);
            _db.PeriodDefinitions.Add(new PeriodDefinition { PeriodNo = i + 1, StartTime = s, EndTime = s.Add(TimeSpan.FromMinutes(55)) });
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
                    string secName = $"{sem}{rule.Prefix}{i}";
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
        if (await _db.Faculty.AnyAsync()) return;
        var rows = Read("faculty.csv");
        if (rows.Count == 0) return;
        if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeding {Count} faculty with logins...", rows.Count);
        var hash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), _o.SeedFacultyPassword);   // hashed ONCE
        var roleIds = await _db.Roles.ToDictionaryAsync(r => r.Name!, r => r.Id);
        var branches = await _db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Code);
        var existingEmails = (await _db.Users.Select(u => u.NormalizedEmail).ToListAsync()).ToHashSet();

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
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
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
                    string secName = $"{sem}{r["section"]}";
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
            foreach (int sem in new[] { 1, 3, 5 })
            {
                var sectionStudents = await _db.Students.Where(s => s.BranchId == b.Id && s.SectionId == sec.Id && s.Semester == sem).ToListAsync();
                int currentCount = sectionStudents.Count;
                if (currentCount >= 30) continue;

                for (int i = currentCount + 1; i <= 30; i++)
                {
                    string studentId = $"{sem}{sec.Name}-{b.Code}-{i:D3}";
                    string email = $"{studentId}@attendx.edu.in";
                    string normEmail = email.ToUpperInvariant();
                    if (existingNormalizedEmails.Contains(normEmail) || existingStudentIDs.Contains(studentId)) continue;
                    existingNormalizedEmails.Add(normEmail);
                    existingStudentIDs.Add(studentId);

                    var user = NewUser(email, $"Student {studentId}", hash);
                    _db.Users.Add(user);
                    _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = studentRole });
                    _db.Students.Add(new Student
                    {
                        StudentID = studentId,
                        FullName = $"Student {studentId}",
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
        _db.ChangeTracker.AutoDetectChangesEnabled = true;
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
        var scheduledSlots = new HashSet<(int SectionId, DayOfWeek Day, int Period)>();
        var facultySlots = new HashSet<(int FacultyId, DayOfWeek Day, int Period)>();
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        int n = 0;
        foreach (var r in rows)
        {
            if (!branches.TryGetValue(r["branchcode"], out var b)) continue;
            int sem = int.Parse(r["semester"]);
            string secName = $"{sem}{r["section"]}";
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

    private async Task SeedNoticesAsync()
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
    private async Task SeedAttendanceHistoryAsync()
    {
        if (await _db.Attendance.AnyAsync()) return;
        var tts = await _db.Timetables.AsNoTracking().ToListAsync();
        var students = await _db.Students.AsNoTracking().Select(s => new { s.Id, s.BranchId, s.SectionId, s.Semester }).ToListAsync();
        if (tts.Count == 0 || students.Count == 0) return;
        if (_log.IsEnabled(LogLevel.Information)) _log.LogInformation("Seeding {Days} days of attendance history (this is the slowest step)...", _o.SeedHistoryDays);

        var rnd = new Random(2026);
        var today = DateTime.Today;
        var holidays = await new CalendarService(_db, new SystemClock()).HolidayDatesAsync(today.AddDays(-_o.SeedHistoryDays - 1), today);
        var byClass = students.GroupBy(s => (s.BranchId, s.SectionId, s.Semester)).ToDictionary(g => g.Key, g => g.ToList());
        var prop = students.ToDictionary(s => s.Id, s => { var r = rnd.NextDouble(); return r < 0.2 ? 0.55 + rnd.NextDouble() * 0.17 : r < 0.8 ? 0.78 + rnd.NextDouble() * 0.12 : 0.92 + rnd.NextDouble() * 0.06; });
        var decliners = students.Where(_ => rnd.NextDouble() < 0.08).Select(s => s.Id).ToHashSet();

        var days = Enumerable.Range(1, Math.Max(1, _o.SeedHistoryDays)).Select(i => today.AddDays(-i))
            .Where(d => d.DayOfWeek != DayOfWeek.Sunday && !holidays.Contains(d)).OrderBy(d => d).ToList();
        var jobs = days.SelectMany(d => tts.Where(t => t.Day == d.DayOfWeek).Select(t => (Date: d, T: t))).ToList();

        await _db.Database.OpenConnectionAsync();
        try
        {
            var conn = (SqlConnection)_db.Database.GetDbConnection();
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
                using var bulk = new SqlBulkCopy(conn) { DestinationTableName = "dbo.Attendance", BatchSize = 10000, BulkCopyTimeout = 600 };
                foreach (DataColumn c in table.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
                await bulk.WriteToServerAsync(table);
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
