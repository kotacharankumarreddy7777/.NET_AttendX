using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;

namespace AttendX.Services;

public interface IFacultyService
{
    Task<(Faculty? Faculty, string? TempPassword, string? Error)> CreateAsync(FacultyFormViewModel m, string? fixedPassword = null, bool mustChange = true);
    Task<(bool Ok, string? Error)> UpdateAsync(int id, FacultyFormViewModel m);
    Task<(bool Ok, string? Error)> SetActiveAsync(int id, bool active);
    Task<ImportResult> ImportCsvAsync(string csv, string? fixedPassword = null, bool mustChange = true);
}

/// <summary>Creating a faculty creates the ASP.NET Identity login too. Only the Identity password HASH exists - no second password is stored.</summary>
public class FacultyService(AppDbContext db, UserManager<ApplicationUser> users, IAuditService audit) : IFacultyService
{
    private readonly AppDbContext _db = db;
    private readonly UserManager<ApplicationUser> _users = users;
    private readonly IAuditService _audit = audit;

    public static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnpqrstuvwxyz", digits = "23456789", sym = "@#$%&*!";
        var chars = new List<char> { Pick(upper), Pick(lower), Pick(digits), Pick(sym) };
        const string all = upper + lower + digits + sym;
        while (chars.Count < 12) chars.Add(Pick(all));
        return new string([.. chars.OrderBy(_ => RandomNumberGenerator.GetInt32(1000))]);
        static char Pick(string s) => s[RandomNumberGenerator.GetInt32(s.Length)];
    }

    private async Task<string> NextFacultyIdAsync()
    {
        var ids = await _db.Faculty.Select(f => f.FacultyID).ToListAsync();
        int max = 0;
        foreach (var id in ids)
            if (id.StartsWith("FAC") && int.TryParse(id.AsSpan(3), out var n) && n > max) max = n;
        return "FAC" + (max + 1).ToString("000");
    }

    public async Task<(Faculty? Faculty, string? TempPassword, string? Error)> CreateAsync(FacultyFormViewModel m, string? fixedPassword = null, bool mustChange = true)
    {
        var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == m.BranchId);
        if (branch == null) return (null, null, "Selected department is invalid.");
        if (string.IsNullOrWhiteSpace(m.Designation) || !FacultyFormViewModel.Designations.Contains(m.Designation.Trim(), StringComparer.OrdinalIgnoreCase))
            return (null, null, $"Invalid designation. Use one of: {string.Join(", ", FacultyFormViewModel.Designations)}.");
        var email = m.Email.Trim();
        var emailUpper = email.ToUpperInvariant();
        if (await _db.Faculty.AnyAsync(f => f.Email.ToUpper() == emailUpper) || await _users.FindByEmailAsync(email) != null)
            return (null, null, "A user with this email already exists.");
        var facultyId = string.IsNullOrWhiteSpace(m.FacultyID) ? await NextFacultyIdAsync() : m.FacultyID.Trim();
        if (await _db.Faculty.AnyAsync(f => f.FacultyID == facultyId)) return (null, null, $"Faculty ID {facultyId} already exists.");

        var password = fixedPassword ?? GeneratePassword();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = m.FullName.Trim(), MustChangePassword = mustChange };
        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded) return (null, null, string.Join(" ", created.Errors.Select(e => e.Description)));
        await _users.AddToRoleAsync(user, RoleNames.Faculty);
        if (m.IsHOD) await _users.AddToRoleAsync(user, RoleNames.Hod);

        var f = new Faculty
        {
            FacultyID = facultyId, FullName = m.FullName.Trim(), Email = email, Phone = m.Phone.Trim(),
            BranchId = branch.Id, Department = branch.Name, Designation = m.Designation.Trim(), IsHOD = m.IsHOD,
            IdentityUserId = user.Id, IsActive = true
        };
        try
        {
            _db.Faculty.Add(f);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            _db.Entry(f).State = EntityState.Detached;
            await _users.DeleteAsync(user);
            if (await _db.Faculty.AnyAsync(x => x.FacultyID == facultyId))
                return (null, null, $"Faculty ID {facultyId} already exists. Reload and try again.");
            return (null, null, "Could not save the faculty (duplicate data?).");
        }
        await _audit.LogAsync("FacultyCreated", "Faculty", f.FacultyID, null, Snapshot(f), branchId: f.BranchId);
        return (f, fixedPassword == null ? password : null, null);
    }

    public async Task<(bool Ok, string? Error)> UpdateAsync(int id, FacultyFormViewModel m)
    {
        var f = await _db.Faculty.FirstOrDefaultAsync(x => x.Id == id);
        if (f == null) return (false, "Faculty not found.");
        var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == m.BranchId);
        if (branch == null) return (false, "Selected department is invalid.");
        if (string.IsNullOrWhiteSpace(m.Designation) || !FacultyFormViewModel.Designations.Contains(m.Designation.Trim(), StringComparer.OrdinalIgnoreCase))
            return (false, $"Invalid designation. Use one of: {string.Join(", ", FacultyFormViewModel.Designations)}.");
        var email = m.Email.Trim();
        var emailUpper = email.ToUpperInvariant();
        if (await _db.Faculty.AnyAsync(x => x.Email.ToUpper() == emailUpper && x.Id != id)) return (false, "Another faculty uses this email.");

        var old = Snapshot(f);
        var user = f.IdentityUserId == null ? null : await _users.FindByIdAsync(f.IdentityUserId);
        if (user != null)
        {
            if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                var other = await _users.FindByEmailAsync(email);
                if (other != null && other.Id != user.Id) return (false, "That email is already used by another login.");
                await _users.SetEmailAsync(user, email);
                await _users.SetUserNameAsync(user, email);
            }
            user.FullName = m.FullName.Trim();
            await _users.UpdateAsync(user);
            bool isHodRole = await _users.IsInRoleAsync(user, RoleNames.Hod);
            if (m.IsHOD && !isHodRole) await _users.AddToRoleAsync(user, RoleNames.Hod);
            if (!m.IsHOD && isHodRole) await _users.RemoveFromRoleAsync(user, RoleNames.Hod);
        }
        f.FullName = m.FullName.Trim(); f.Email = email; f.Phone = m.Phone.Trim();
        f.BranchId = branch.Id; f.Department = branch.Name; f.Designation = m.Designation.Trim(); f.IsHOD = m.IsHOD;
        f.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("FacultyUpdated", "Faculty", f.FacultyID, old, Snapshot(f), branchId: f.BranchId);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetActiveAsync(int id, bool active)
    {
        var f = await _db.Faculty.FirstOrDefaultAsync(x => x.Id == id);
        if (f == null) return (false, "Faculty not found.");
        f.IsActive = active; f.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        var user = f.IdentityUserId == null ? null : await _users.FindByIdAsync(f.IdentityUserId);
        if (user != null)
        {
            await _users.SetLockoutEnabledAsync(user, true);
            await _users.SetLockoutEndDateAsync(user, active ? null : DateTimeOffset.MaxValue);   // inactive => cannot sign in
        }
        await _audit.LogAsync(active ? "FacultyActivated" : "FacultyDeactivated", "Faculty", f.FacultyID, branchId: f.BranchId);
        return (true, null);
    }

    /// <summary>CSV columns: FacultyID(optional),FullName,Email,Phone,Department,Designation,IsHOD. Valid rows are imported in ONE transaction; invalid rows are reported.</summary>
    public async Task<ImportResult> ImportCsvAsync(string csv, string? fixedPassword = null, bool mustChange = true)
    {
        var result = new ImportResult();
        var rows = CsvParser.ParseWithHeader(csv);
        var branches = await _db.Branches.AsNoTracking().ToListAsync();
        var existingEmails = (await _db.Faculty.Select(f => f.Email).ToListAsync()).Select(e => e.ToLowerInvariant()).ToHashSet();
        var existingIds = (await _db.Faculty.Select(f => f.FacultyID).ToListAsync()).ToHashSet();
        var seenEmails = new HashSet<string>();
        var valid = new List<FacultyFormViewModel>();
        var emailRx = new EmailAddressAttribute();
        int line = 1;

        foreach (var r in rows)
        {
            line++; result.Total++;
            string Get(string k) => r.TryGetValue(k, out var v) ? v : "";
            var dept = Get("department");
            var branch = branches.FirstOrDefault(b => b.Code.Equals(dept, StringComparison.OrdinalIgnoreCase) || b.Name.Equals(dept, StringComparison.OrdinalIgnoreCase));
            var vm = new FacultyFormViewModel
            {
                FacultyID = Get("facultyid"), FullName = Get("fullname"), Email = Get("email"), Phone = Get("phone"),
                BranchId = branch?.Id ?? 0, Designation = Get("designation"),
                IsHOD = Get("ishod").Equals("true", StringComparison.OrdinalIgnoreCase) || Get("ishod") == "1"
            };
            if (string.IsNullOrWhiteSpace(vm.FullName) || string.IsNullOrWhiteSpace(vm.Email) || string.IsNullOrWhiteSpace(vm.Phone) || string.IsNullOrWhiteSpace(vm.Designation))
            { result.Errors.Add($"Line {line}: FullName, Email, Phone, Department and Designation are required."); continue; }
            if (!emailRx.IsValid(vm.Email)) { result.Errors.Add($"Line {line}: invalid email '{vm.Email}'."); continue; }
            if (branch == null) { result.Errors.Add($"Line {line}: unknown department '{dept}'."); continue; }
            if (!FacultyFormViewModel.Designations.Contains(vm.Designation, StringComparer.OrdinalIgnoreCase))
            { result.Errors.Add($"Line {line}: invalid designation '{vm.Designation}' (use: {string.Join(", ", FacultyFormViewModel.Designations)})."); continue; }
            var key = vm.Email.Trim().ToLowerInvariant();
            if (existingEmails.Contains(key) || !seenEmails.Add(key)) { result.Errors.Add($"Line {line}: duplicate email '{vm.Email}'."); continue; }
            if (!string.IsNullOrWhiteSpace(vm.FacultyID) && existingIds.Contains(vm.FacultyID.Trim())) { result.Errors.Add($"Line {line}: Faculty ID '{vm.FacultyID}' already exists."); continue; }
            valid.Add(vm);
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var vm in valid)
            {
                var (f, _, err) = await CreateAsync(vm, fixedPassword, mustChange);
                if (f == null) throw new InvalidOperationException(err ?? "Unknown error while creating " + vm.Email);
                result.Imported++;
            }
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            result.Errors.Add("Import rolled back, nothing was saved: " + ex.Message);
            result.Imported = 0;
        }
        await _audit.LogAsync("FacultyImported", "Faculty", null, details: $"CSV import: {result.Imported}/{result.Total} imported, {result.Errors.Count} issue(s)");
        return result;
    }

    private static string Snapshot(Faculty f) => JsonSerializer.Serialize(new { f.FacultyID, f.FullName, f.Email, f.Phone, f.BranchId, f.Designation, f.IsHOD, f.IsActive });
}
