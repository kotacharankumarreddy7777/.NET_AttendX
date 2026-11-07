using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AttendX.Services;

public interface IBackupService
{
    Task<BackupViewModel> GetAsync();
    Task<(bool Ok, string Message)> CreateAsync();
    Task<(bool Ok, string Message)> RestoreAsync(string fileName);
}

public class BackupService(AppDbContext db, IConfiguration cfg, IOptions<AttendXOptions> o, IWebHostEnvironment env, IAuditService audit, ICurrentUser user) : IBackupService
{
    private readonly AppDbContext _db = db;
    private readonly IConfiguration _cfg = cfg;
    private readonly AttendXOptions _o = o.Value;
    private readonly IWebHostEnvironment _env = env;
    private readonly IAuditService _audit = audit;
    private readonly ICurrentUser _user = user;

    private string ConnectionString => ConnectionHelper.ConvertToNpgsqlConnectionString(
        Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
        ?? _cfg.GetConnectionString("DefaultConnection")
        ?? "");

    private bool IsPostgres => ConnectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
                              || ConnectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                              || ConnectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
                              || Environment.GetEnvironmentVariable("USE_POSTGRES") == "true";

    private string DbName
    {
        get
        {
            if (IsPostgres)
            {
                try
                {
                    var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
                    return builder.Database ?? "AttendXDb";
                }
                catch
                {
                    return "AttendXDb";
                }
            }
            else
            {
                try
                {
                    var builder = new SqlConnectionStringBuilder(ConnectionString);
                    return builder.InitialCatalog;
                }
                catch
                {
                    return "AttendXDb";
                }
            }
        }
    }

    private string Folder
    {
        get
        {
            var f = string.IsNullOrWhiteSpace(_o.BackupFolder) ? Path.Combine(_env.ContentRootPath, "App_Data", "Backups") : _o.BackupFolder;
            Directory.CreateDirectory(f);
            return f;
        }
    }
    private static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";

    public async Task<BackupViewModel> GetAsync()
    {
        var files = new DirectoryInfo(Folder).GetFiles("*.bak").OrderByDescending(f => f.CreationTime)
            .Select(f => new BackupFileInfo { FileName = f.Name, SizeBytes = f.Length, CreatedAt = f.CreationTime }).ToList();
        var hist = await _db.BackupHistories.AsNoTracking().OrderByDescending(h => h.At).Take(30).ToListAsync();
        return new BackupViewModel { Files = files, History = hist, Folder = Folder, DatabaseName = DbName };
    }

    public async Task<(bool Ok, string Message)> CreateAsync()
    {
        if (IsPostgres)
        {
            return (false, "Database backups on PostgreSQL are managed via Supabase/Railway automated backups.");
        }

        var fileName = $"{DbName}_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
        var path = Path.Combine(Folder, fileName);
        try
        {
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 600;
            cmd.CommandText = $"BACKUP DATABASE {Quote(DbName)} TO DISK = @p WITH INIT, CHECKSUM";
            cmd.Parameters.AddWithValue("@p", path);
            await cmd.ExecuteNonQueryAsync();
            var size = new FileInfo(path).Length;
            _db.BackupHistories.Add(new BackupHistory { Action = "Backup", FileName = fileName, SizeBytes = size, Status = "Success", By = _user.DisplayName });
            await _db.SaveChangesAsync();
            await _audit.LogAsync("BackupCreated", "Database", fileName);
            return (true, $"Backup created: {fileName} ({size / 1024} KB).");
        }
        catch (Exception ex)
        {
            _db.BackupHistories.Add(new BackupHistory { Action = "Backup", FileName = fileName, Status = "Failed", Message = Short(ex.Message), By = _user.DisplayName });
            await _db.SaveChangesAsync();
            return (false, "Backup failed: " + Short(ex.Message));
        }
    }

    public async Task<(bool Ok, string Message)> RestoreAsync(string fileName)
    {
        if (IsPostgres)
        {
            return (false, "Database restoration on PostgreSQL is managed via Supabase/Railway.");
        }

        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            return (false, "Invalid backup file name.");
        var path = Path.Combine(Folder, fileName);
        if (!File.Exists(path)) return (false, "Backup file not found.");

        var masterBuilder = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master", Pooling = false };
        var db = Quote(DbName);
        try
        {
            await using var conn = new SqlConnection(masterBuilder.ConnectionString);
            await conn.OpenAsync();
            await using (var v = conn.CreateCommand())
            {
                v.CommandText = "RESTORE VERIFYONLY FROM DISK = @p";
                v.Parameters.AddWithValue("@p", path);
                v.CommandTimeout = 600;
                await v.ExecuteNonQueryAsync();
            }
            try
            {
                await using var r = conn.CreateCommand();
                r.CommandTimeout = 1800;
                r.CommandText = $"ALTER DATABASE {db} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE {db} FROM DISK = @p WITH REPLACE;";
                r.Parameters.AddWithValue("@p", path);
                await r.ExecuteNonQueryAsync();
            }
            finally
            {
                await using var m = conn.CreateCommand();
                m.CommandText = $"ALTER DATABASE {db} SET MULTI_USER;";
                await m.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            return (false, "Restore failed: " + Short(ex.Message));
        }
        SqlConnection.ClearAllPools();
        _db.ChangeTracker.Clear();
        _db.BackupHistories.Add(new BackupHistory { Action = "Restore", FileName = fileName, Status = "Success", By = _user.DisplayName });
        await _db.SaveChangesAsync();
        await _audit.LogAsync("BackupRestored", "Database", fileName);
        return (true, $"Database restored from {fileName}. Please restart the application and sign in again.");
    }

    private static string Short(string s) => s.Length > 300 ? s[..300] : s;
}
