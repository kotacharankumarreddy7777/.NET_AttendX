using Npgsql;
using Microsoft.Extensions.Options;

namespace AttendX.Services;

public interface IBackupService
{
    Task<BackupViewModel> GetAsync();
    Task<(bool Ok, string Message)> CreateAsync();
    Task<(bool Ok, string Message)> RestoreAsync(string fileName);
}

/// <summary>PostgreSQL pg_dump / pg_restore based backup/restore.</summary>
public class BackupService(AppDbContext db, IConfiguration cfg, IOptions<AttendXOptions> o, IWebHostEnvironment env, IAuditService audit, ICurrentUser user) : IBackupService
{
    private readonly AppDbContext _db = db;
    private readonly IConfiguration _cfg = cfg;
    private readonly AttendXOptions _o = o.Value;
    private readonly IWebHostEnvironment _env = env;
    private readonly IAuditService _audit = audit;
    private readonly ICurrentUser _user = user;

    private NpgsqlConnectionStringBuilder Builder => new(_cfg.GetConnectionString("DefaultConnection"));
    private string DbName => Builder.Database!;
    private string Folder
    {
        get
        {
            var f = string.IsNullOrWhiteSpace(_o.BackupFolder) ? Path.Combine(_env.ContentRootPath, "App_Data", "Backups") : _o.BackupFolder;
            Directory.CreateDirectory(f);
            return f;
        }
    }

    public async Task<BackupViewModel> GetAsync()
    {
        var files = new DirectoryInfo(Folder).GetFiles("*.*").Where(f => f.Extension is ".backup" or ".json").OrderByDescending(f => f.CreationTime)
            .Select(f => new BackupFileInfo { FileName = f.Name, SizeBytes = f.Length, CreatedAt = f.CreationTime }).ToList();
        var hist = await _db.BackupHistories.AsNoTracking().OrderByDescending(h => h.At).Take(30).ToListAsync();
        return new BackupViewModel { Files = files, History = hist, Folder = Folder, DatabaseName = DbName };
    }

    public async Task<(bool Ok, string Message)> CreateAsync()
    {
        var fileName = $"{DbName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.backup";
        var path = Path.Combine(Folder, fileName);
        try
        {
            // Use pg_dump for PostgreSQL backup
            var connStr = Builder.ConnectionString;
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pg_dump",
                Arguments = $"-d \"{connStr}\" -f \"{path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Could not start pg_dump process.");
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
                return (false, $"pg_dump failed: {stderr}");

            var size = new FileInfo(path).Length;
            _db.BackupHistories.Add(new BackupHistory { Action = "Backup", FileName = fileName, SizeBytes = size, Status = "Success", By = _user.DisplayName });
            await _db.SaveChangesAsync();
            await _audit.LogAsync("BackupCreated", "Database", fileName);
            return (true, $"Backup created: {fileName} ({size / 1024} KB).");
        }
        catch (Exception ex)
        {
            // pg_dump unavailable (e.g. Neon/Render without pg_dump on PATH) - fall back to JSON export
            _db.ChangeTracker.Clear();
            var jsonName = $"{DbName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var jsonPath = Path.Combine(Folder, jsonName);
            try
            {
                var data = new
                {
                    Database = DbName,
                    CreatedAt = DateTime.UtcNow,
                    Branches = await _db.Branches.AsNoTracking().ToListAsync(),
                    Sections = await _db.Sections.AsNoTracking().ToListAsync(),
                    Subjects = await _db.Subjects.AsNoTracking().ToListAsync(),
                    Faculty = await _db.Faculty.AsNoTracking().ToListAsync(),
                    Students = await _db.Students.AsNoTracking().ToListAsync(),
                    Timetables = await _db.Timetables.AsNoTracking().ToListAsync(),
                    Exams = await _db.Exams.AsNoTracking().ToListAsync(),
                    ExamTimetables = await _db.ExamTimetables.AsNoTracking().ToListAsync(),
                    ExamMarks = await _db.ExamMarks.AsNoTracking().ToListAsync(),
                    Holidays = await _db.Holidays.AsNoTracking().ToListAsync(),
                    Notices = await _db.Notices.AsNoTracking().ToListAsync()
                };
                await System.IO.File.WriteAllTextAsync(jsonPath, System.Text.Json.JsonSerializer.Serialize(data));
                var size = new FileInfo(jsonPath).Length;
                _db.BackupHistories.Add(new BackupHistory { Action = "Backup", FileName = jsonName, SizeBytes = size, Status = "Success", By = _user.DisplayName, Message = "JSON fallback (pg_dump unavailable)" });
                await _db.SaveChangesAsync();
                await _audit.LogAsync("BackupCreated", "Database", jsonName);
                return (true, $"pg_dump unavailable - JSON backup created: {jsonName} ({size / 1024} KB).");
            }
            catch (Exception jex)
            {
                _db.BackupHistories.Add(new BackupHistory { Action = "Backup", FileName = jsonName, Status = "Failed", Message = jex.Message.Length > 300 ? jex.Message[..300] : jex.Message, By = _user.DisplayName });
                await _db.SaveChangesAsync();
                return (false, "Backup failed: " + (ex.Message.Length > 300 ? ex.Message[..300] : ex.Message) + " (ensure pg_dump is installed and in PATH). JSON fallback also failed: " + jex.Message);
            }
        }
    }

    public async Task<(bool Ok, string Message)> RestoreAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            return (false, "Invalid backup file name.");
        var path = Path.Combine(Folder, fileName);
        if (!File.Exists(path)) return (false, "Backup file not found.");

        // JSON backup restore (Neon/Render fallback path)
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(File.OpenRead(path));
                var root = doc.RootElement;
                _db.ChangeTracker.Clear();
                // Only restore master data (branches, sections, subjects, faculty, students, timetables, exams, holidays, notices).
                // Attendance/ExamMarks are high-volume; restoring them from JSON is out of scope for the fallback.
                async Task RestoreSet<T>(string propName) where T : class
                {
                    if (!root.TryGetProperty(propName, out var arr) || arr.ValueKind != System.Text.Json.JsonValueKind.Array) return;
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<T>>(arr.GetRawText());
                    if (list == null || list.Count == 0) return;
                    _db.Set<T>().RemoveRange(_db.Set<T>());
                    await _db.SaveChangesAsync();
                    await _db.Set<T>().AddRangeAsync(list);
                    await _db.SaveChangesAsync();
                }
                await RestoreSet<Branch>("Branches");
                await RestoreSet<Section>("Sections");
                await RestoreSet<Subject>("Subjects");
                await RestoreSet<Faculty>("Faculty");
                await RestoreSet<Student>("Students");
                await RestoreSet<Timetable>("Timetables");
                await RestoreSet<Exam>("Exams");
                await RestoreSet<Holiday>("Holidays");
                await RestoreSet<Notice>("Notices");
                _db.BackupHistories.Add(new BackupHistory { Action = "Restore", FileName = fileName, Status = "Success", By = _user.DisplayName, Message = "JSON master-data restore" });
                await _db.SaveChangesAsync();
                await _audit.LogAsync("BackupRestored", "Database", fileName);
                return (true, $"Master data restored from {fileName}. Please restart the application. (Attendance and exam marks are not included in JSON backups.)");
            }
            catch (Exception jex)
            {
                return (false, "JSON restore failed: " + (jex.Message.Length > 300 ? jex.Message[..300] : jex.Message));
            }
        }

        if (!fileName.EndsWith(".backup", StringComparison.OrdinalIgnoreCase))
            return (false, "Invalid backup file name.");

        try
        {
            var connStr = Builder.ConnectionString;
            // Drop and recreate database then pg_restore
            var dropPsi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "psql",
                Arguments = $"-d \"{connStr}\" -c \"DROP DATABASE IF EXISTS {DbName}; CREATE DATABASE {DbName};\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var dropProc = System.Diagnostics.Process.Start(dropPsi) ?? throw new InvalidOperationException("Could not start psql process.");
            await dropProc.WaitForExitAsync();

            var restorePsi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pg_restore",
                Arguments = $"-d \"{connStr}\" -1 \"{path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var restoreProc = System.Diagnostics.Process.Start(restorePsi) ?? throw new InvalidOperationException("Could not start pg_restore process.");
            var stderr = await restoreProc.StandardError.ReadToEndAsync();
            await restoreProc.WaitForExitAsync();

            if (restoreProc.ExitCode != 0)
                return (false, $"pg_restore failed: {stderr}");

            _db.ChangeTracker.Clear();
            _db.BackupHistories.Add(new BackupHistory { Action = "Restore", FileName = fileName, Status = "Success", By = _user.DisplayName });
            await _db.SaveChangesAsync();
            await _audit.LogAsync("BackupRestored", "Database", fileName);
            return (true, $"Database restored from {fileName}. Please restart the application and sign in again.");
        }
        catch (Exception ex)
        {
            return (false, "Restore failed: " + (ex.Message.Length > 300 ? ex.Message[..300] : ex.Message));
        }
    }
}

