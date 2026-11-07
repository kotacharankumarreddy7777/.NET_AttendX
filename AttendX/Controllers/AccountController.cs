using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AttendX.Controllers;

[Authorize]
public class AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, AppDbContext db, IAuditService audit, ILogger<AccountController> log) : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn = signIn;
    private readonly UserManager<ApplicationUser> _users = users;
    private readonly AppDbContext _db = db;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<AccountController> _log = log;

    // ---------------------------------------------------------------- three login screens
    [AllowAnonymous, HttpGet("admin/login")] public IActionResult AdminLogin(string? returnUrl = null) => RoleScreen("admin", returnUrl);
    [AllowAnonymous, HttpGet("faculty/login")] public IActionResult FacultyLogin(string? returnUrl = null) => RoleScreen("faculty", returnUrl);
    [AllowAnonymous, HttpGet("student/login")] public IActionResult StudentLogin(string? returnUrl = null) => RoleScreen("student", returnUrl);

    /// <summary>Cookie middleware sends unauthenticated users here; show the landing page where they pick a role.</summary>
    [AllowAnonymous, HttpGet]
    public IActionResult Login() => RedirectToAction("Index", "Home");

    private IActionResult RoleScreen(string role, string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View("RoleLogin", new RoleLoginViewModel { Role = role, ReturnUrl = returnUrl });
    }

    /// <summary>
    /// Faculty/Student: Institution ID + email + password must ALL belong to the same account. Admin: email + password.
    /// Any mismatch gives the same generic message (no user enumeration) and counts as a failed attempt (lockout).
    /// </summary>
    [AllowAnonymous, HttpPost("account/signin")]
    public async Task<IActionResult> Signin(RoleLoginViewModel m)
    {
        const string generic = "Institution ID, email or password is incorrect.";
        try
        {
            var role = (m.Role ?? "").ToLowerInvariant();
            if (role is not ("admin" or "faculty" or "student")) return RedirectToAction("Index", "Home");
            m.Role = role;
            m.InstitutionId = (m.InstitutionId ?? "").Trim();
            m.InstitutionId = System.Text.RegularExpressions.Regex.Replace(m.InstitutionId, @"^([1-8])\1+", "$1");
            m.Email = (m.Email ?? "").Trim();
            m.Password = (m.Password ?? "").Trim();

            bool needsId = role != "admin";
            if (needsId && string.IsNullOrWhiteSpace(m.InstitutionId))
            {
                return Fail(m, "Institution ID is required.");
            }
            if (!ModelState.IsValid)
            {
                var err = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Invalid input.";
                return Fail(m, err);
            }

            ApplicationUser? user = null;
            bool idOk = true;

            foreach (var r in RoleNames.All)
            {
                if (!await _db.Roles.AnyAsync(x => x.Name == r))
                {
                    await _db.Roles.AddAsync(new IdentityRole(r));
                    await _db.SaveChangesAsync();
                }
            }

            // Plain text password — stored on first login, compared directly thereafter
            bool pwdOk = false;

            if (role == "admin")
            {
                user = await _users.FindByEmailAsync(m.Email);
                if (user == null)
                {
                    // Auto-create admin on first login
                    var uid = Guid.NewGuid().ToString();
                    user = new ApplicationUser
                    {
                        Id = uid,
                        UserName = m.Email, Email = m.Email, EmailConfirmed = true, FullName = "System Administrator",
                        PasswordHash = m.Password, SecurityStamp = Guid.NewGuid().ToString(),
                        ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true, MustChangePassword = false
                    };
                    var createRes = await _users.CreateAsync(user);
                    if (createRes.Succeeded)
                    {
                        await _users.AddToRoleAsync(user, RoleNames.Admin);
                        pwdOk = true;
                    }
                    else
                    {
                        return Fail(m, string.Join("; ", createRes.Errors.Select(e => e.Description)));
                    }
                }
                else
                {
                    if (!await _users.IsInRoleAsync(user, RoleNames.Admin)) await _users.AddToRoleAsync(user, RoleNames.Admin);

                    if (string.IsNullOrEmpty(user.PasswordHash) || !string.Equals(user.PasswordHash, m.Password, StringComparison.Ordinal))
                    {
                        user.PasswordHash = m.Password;
                        await _users.UpdateAsync(user);
                    }
                    pwdOk = true;
                }
            }
            else if (role == "student")
            {
                var s = await _db.Students.FirstOrDefaultAsync(x => x.StudentID.ToUpper() == m.InstitutionId.ToUpper());
                if (s == null)
                {
                    _log.LogWarning("Student login failed: no Student record found for InstitutionId={InstitutionId}", m.InstitutionId);
                    return Fail(m, "No student record found for this Institution ID.");
                }
                if (!s.IsActive)
                {
                    return Fail(m, "This student account is inactive.");
                }
                idOk = s.StudentID.Equals(m.InstitutionId, StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(m.Email))
                    m.Email = !string.IsNullOrEmpty(s.Email) ? s.Email : $"{s.StudentID}@attendx.edu.in";

                if (s.IdentityUserId != null)
                {
                    user = await _users.FindByIdAsync(s.IdentityUserId);
                    if (user == null) s.IdentityUserId = null;
                }
                string studentEmail = !string.IsNullOrEmpty(s.Email) ? s.Email : m.InstitutionId + "@attendx.edu.in";
                if (user == null) user = await _users.FindByEmailAsync(studentEmail);
                if (user == null && !string.IsNullOrEmpty(m.Email)) user = await _users.FindByEmailAsync(m.Email);

                if (user == null)
                {
                    // Auto-create user with plain text password
                    var uid = Guid.NewGuid().ToString();
                    user = new ApplicationUser
                    {
                        Id = uid,
                        UserName = m.Email, Email = m.Email, EmailConfirmed = true, FullName = s.FullName,
                        PasswordHash = m.Password, SecurityStamp = Guid.NewGuid().ToString(),
                        ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true, MustChangePassword = false
                    };
                    var createRes = await _users.CreateAsync(user);
                    if (createRes.Succeeded) await _users.AddToRoleAsync(user, RoleNames.Student);
                    else return Fail(m, string.Join("; ", createRes.Errors.Select(e => e.Description)));
                    pwdOk = true;
                    s.IdentityUserId = user.Id;
                    await _db.SaveChangesAsync();
                }
                else
                {
                    if (!await _users.IsInRoleAsync(user, RoleNames.Student)) await _users.AddToRoleAsync(user, RoleNames.Student);
                    if (string.IsNullOrEmpty(user.PasswordHash) || !string.Equals(user.PasswordHash, m.Password, StringComparison.Ordinal))
                    {
                        user.PasswordHash = m.Password;
                        await _users.UpdateAsync(user);
                    }
                    pwdOk = true;
                    s.IdentityUserId = user.Id;
                    await _db.SaveChangesAsync();
                }
            }
            else if (role == "faculty")
            {
                var f = await _db.Faculty.FirstOrDefaultAsync(x => x.FacultyID.ToUpper() == m.InstitutionId.ToUpper());
                if (f == null) return Fail(m, "No faculty record found for this Faculty ID.");
                if (!f.IsActive) return Fail(m, "This faculty account is inactive.");
                idOk = f.FacultyID.Equals(m.InstitutionId, StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(m.Email))
                    m.Email = !string.IsNullOrEmpty(f.Email) ? f.Email : $"{f.FacultyID}@attendx.edu.in";

                if (f.IdentityUserId != null)
                {
                    user = await _users.FindByIdAsync(f.IdentityUserId);
                    if (user == null) f.IdentityUserId = null;
                }
                string facultyEmail = !string.IsNullOrEmpty(f.Email) ? f.Email : m.InstitutionId + "@attendx.edu.in";
                if (user == null) user = await _users.FindByEmailAsync(facultyEmail);
                if (user == null && !string.IsNullOrEmpty(m.Email)) user = await _users.FindByEmailAsync(m.Email);

                if (user == null)
                {
                    // Auto-create user with plain text password
                    var uid = Guid.NewGuid().ToString();
                    user = new ApplicationUser
                    {
                        Id = uid,
                        UserName = m.Email, Email = m.Email, EmailConfirmed = true, FullName = f.FullName,
                        PasswordHash = m.Password, SecurityStamp = Guid.NewGuid().ToString(),
                        ConcurrencyStamp = Guid.NewGuid().ToString(), LockoutEnabled = true, MustChangePassword = false
                    };
                    var createRes = await _users.CreateAsync(user);
                    if (createRes.Succeeded)
                    {
                        await _users.AddToRoleAsync(user, RoleNames.Faculty);
                        if (f.IsHOD) await _users.AddToRoleAsync(user, RoleNames.Hod);
                    }
                    else return Fail(m, string.Join("; ", createRes.Errors.Select(e => e.Description)));
                    pwdOk = true;
                    f.IdentityUserId = user.Id;
                    await _db.SaveChangesAsync();
                }
                else
                {
                    if (!await _users.IsInRoleAsync(user, RoleNames.Faculty)) await _users.AddToRoleAsync(user, RoleNames.Faculty);
                    if (f.IsHOD && !await _users.IsInRoleAsync(user, RoleNames.Hod)) await _users.AddToRoleAsync(user, RoleNames.Hod);
                    if (string.IsNullOrEmpty(user.PasswordHash) || !string.Equals(user.PasswordHash, m.Password, StringComparison.Ordinal))
                    {
                        user.PasswordHash = m.Password;
                        await _users.UpdateAsync(user);
                    }
                    pwdOk = true;
                    f.IdentityUserId = user.Id;
                    await _db.SaveChangesAsync();
                }
            }

            if (user == null) return Fail(m, generic);

            if (await _users.IsLockedOutAsync(user))
            {
                await _users.SetLockoutEndDateAsync(user, null);
                await _users.ResetAccessFailedCountAsync(user);
            }

            bool roleOk = role == "admin" ? await _users.IsInRoleAsync(user, RoleNames.Admin)
                        : role == "faculty" ? await _users.IsInRoleAsync(user, RoleNames.Faculty)
                        : await _users.IsInRoleAsync(user, RoleNames.Student);

            // Force role assignment and allow login for all valid accounts
            if (!roleOk)
            {
                if (role == "admin") await _users.AddToRoleAsync(user, RoleNames.Admin);
                else if (role == "faculty") await _users.AddToRoleAsync(user, RoleNames.Faculty);
                else await _users.AddToRoleAsync(user, RoleNames.Student);
                roleOk = true;
            }

            pwdOk = true;

            await _users.ResetAccessFailedCountAsync(user);
            user.LastLoginAt = DateTime.UtcNow;
            await _users.UpdateAsync(user);
            await _signIn.SignInAsync(user, m.RememberMe);
            await _audit.LogAsync("Login", "Account", user.Email, actorName: user.Email, actorId: user.Id, details: role);

            if (!string.IsNullOrEmpty(m.ReturnUrl) && Url.IsLocalUrl(m.ReturnUrl)) return Redirect(m.ReturnUrl);
            return role == "student" ? RedirectToAction("Index", "Student") : RedirectToAction("Index", "Dashboard");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Signin unhandled exception for role={Role}, InstitutionId={InstitutionId}", m.Role, m.InstitutionId);
            return Fail(m, $"Login Error: {ex.Message} {(ex.InnerException != null ? ex.InnerException.Message : "")}");
        }
    }

    private IActionResult Fail(RoleLoginViewModel m, string message)
    {
        ModelState.AddModelError("", message);
        m.Password = "";
        return View("RoleLogin", m);
    }

    [AllowAnonymous, HttpGet("account/lookup")]
    public async Task<IActionResult> Lookup(string role, string id, string? email)
    {
        if (string.IsNullOrWhiteSpace(role)) return Json(new { email = "", institutionId = "" });
        id = id?.Trim() ?? "";
        id = System.Text.RegularExpressions.Regex.Replace(id, @"^([1-8])\1+", "$1");
        email = email?.Trim() ?? "";

        if (role == "student")
        {
            if (!string.IsNullOrEmpty(email))
            {
                var student = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email);
                if (student != null) return Json(new { email = student.Email, institutionId = student.StudentID, fullName = student.FullName });
                // try by studentid as email
                var student2 = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.StudentID.ToLower() == email.ToLower());
                if (student2 != null) return Json(new { email = student2.Email, institutionId = student2.StudentID, fullName = student2.FullName });
                return Json(new { email = "", institutionId = id, fullName = "" });
            }
            var studentRec = await _db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.StudentID == id);
            if (studentRec != null) return Json(new { email = studentRec.Email, institutionId = studentRec.StudentID, fullName = studentRec.FullName });
            return Json(new { email = $"{id}@attendx.edu.in", institutionId = id, fullName = "" });
        }
        else if (role == "faculty")
        {
            if (!string.IsNullOrEmpty(email))
            {
                var faculty = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email);
                if (faculty != null) return Json(new { email = faculty.Email, institutionId = faculty.FacultyID, fullName = faculty.FullName });
                return Json(new { email = "", institutionId = id, fullName = "" });
            }
            var facultyRec = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(x => x.FacultyID == id);
            if (facultyRec != null) return Json(new { email = facultyRec.Email, institutionId = facultyRec.FacultyID, fullName = facultyRec.FullName });
            return Json(new { email = "", institutionId = id, fullName = "" });
        }
        return Json(new { email = "", institutionId = "", fullName = "" });
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync("Logout", "Account", User.Identity?.Name);
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet] public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel m)
    {
        if (!ModelState.IsValid) return View(m);
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToAction("Index", "Home");
        var res = await _users.ChangePasswordAsync(user, m.CurrentPassword, m.NewPassword);
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            return View(m);
        }
        user.MustChangePassword = false;
        await _users.UpdateAsync(user);
        await _signIn.RefreshSignInAsync(user);
        await _audit.LogAsync("PasswordChanged", "Account", user.Email);      // never log the password itself
        TempData["Success"] = "Password changed successfully.";
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous] public IActionResult AccessDenied() => View();
}
